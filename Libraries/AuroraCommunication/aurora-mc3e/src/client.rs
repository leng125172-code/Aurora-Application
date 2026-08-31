//! Managed MC 3E TCP/UDP client.

use crate::{
    McAddress, build_ascii_batch, build_binary_batch, parse_ascii_response, parse_binary_response,
};
use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DeviceClient, DeviceDataType,
    DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use aurora_comm_transport::{
    ByteStream, DatagramTransport, TcpTransport, TcpTransportOptions, UdpTransport,
    UdpTransportOptions,
};
use std::str::FromStr;
use std::time::{Duration, SystemTime};
use tokio::sync::{Mutex, watch};

/// MC physical transport.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Mc3eTransport {
    /// Stream transport.
    Tcp,
    /// Datagram transport.
    Udp,
}
/// MC 3E frame representation.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Mc3eFrame {
    /// Binary frame.
    Binary,
    /// ASCII hexadecimal frame.
    Ascii,
}

/// MC 3E endpoint and routing settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Mc3eOptions {
    /// PLC hostname or IP address.
    pub host: String,
    /// PLC port, commonly 5000/5001.
    pub port: u16,
    /// TCP or UDP.
    pub transport: Mc3eTransport,
    /// Binary or ASCII 3E frame.
    pub frame: Mc3eFrame,
    /// MC monitoring timer in 250 ms units.
    pub monitoring_timer: u16,
    /// Connection timeout.
    pub connect_timeout: Duration,
}

impl Mc3eOptions {
    /// Creates binary MC 3E TCP defaults.
    pub fn new(host: impl Into<String>) -> Self {
        Self {
            host: host.into(),
            port: 5000,
            transport: Mc3eTransport::Tcp,
            frame: Mc3eFrame::Binary,
            monitoring_timer: 0x10,
            connect_timeout: Duration::from_secs(5),
        }
    }
}

enum Channel {
    Tcp(TcpTransport),
    Udp(UdpTransport),
}

/// Serialized MC protocol 3E client.
pub struct Mc3eClient {
    channel: Channel,
    frame: Mc3eFrame,
    timer: u16,
    gate: Mutex<()>,
    status_tx: watch::Sender<DeviceStatus>,
}

impl Mc3eClient {
    /// Creates a disconnected TCP or UDP client.
    pub fn new(options: Mc3eOptions) -> Self {
        let channel = match options.transport {
            Mc3eTransport::Tcp => {
                let mut value = TcpTransportOptions::new(options.host, options.port);
                value.connect_timeout = options.connect_timeout;
                Channel::Tcp(TcpTransport::new(value))
            }
            Mc3eTransport::Udp => {
                let mut value = UdpTransportOptions::new(options.host, options.port);
                value.connect_timeout = options.connect_timeout;
                Channel::Udp(UdpTransport::new(value))
            }
        };
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Self {
            channel,
            frame: options.frame,
            timer: options.monitoring_timer,
            gate: Mutex::new(()),
            status_tx,
        }
    }

    async fn exchange(&self, request: &[u8], deadline: Duration) -> CommResult<Vec<u8>> {
        match &self.channel {
            Channel::Udp(value) => value.request_response(request, 65535, deadline).await,
            Channel::Tcp(value) => {
                value.write_all(request, deadline).await?;
                match self.frame {
                    Mc3eFrame::Binary => {
                        let mut header = value.read_exact(9, deadline).await?;
                        let len = usize::from(u16::from_le_bytes([header[7], header[8]]));
                        header.extend(value.read_exact(len, deadline).await?);
                        Ok(header)
                    }
                    Mc3eFrame::Ascii => {
                        let mut header = value.read_exact(18, deadline).await?;
                        let text = std::str::from_utf8(&header[14..18])
                            .map_err(|_| protocol("invalid ASCII length"))?;
                        let len = usize::from_str_radix(text, 16)
                            .map_err(|_| protocol("invalid ASCII length"))?;
                        header.extend(value.read_exact(len, deadline).await?);
                        Ok(header)
                    }
                }
            }
        }
    }
    async fn set_connected(&self, connect: bool) -> CommResult<()> {
        match &self.channel {
            Channel::Tcp(v) => {
                if connect {
                    v.connect().await
                } else {
                    v.disconnect().await
                }
            }
            Channel::Udp(v) => {
                if connect {
                    v.connect().await
                } else {
                    v.disconnect().await
                }
            }
        }
    }
    fn publish(&self, state: ConnectionState, error: Option<&CommError>) {
        self.status_tx.send_replace(DeviceStatus {
            state,
            changed_at: SystemTime::now(),
            last_error_code: error.map(|v| v.code.clone()),
            reconnect_attempt: 0,
        });
    }
}

#[async_trait]
impl DeviceClient for Mc3eClient {
    async fn connect(&self) -> CommResult<()> {
        self.publish(ConnectionState::Connecting, None);
        let r = self.set_connected(true).await;
        self.publish(
            if r.is_ok() {
                ConnectionState::Connected
            } else {
                ConnectionState::Faulted
            },
            r.as_ref().err(),
        );
        r
    }
    async fn disconnect(&self) -> CommResult<()> {
        let r = self.set_connected(false).await;
        self.publish(ConnectionState::Disconnected, r.as_ref().err());
        r
    }
    async fn read(&self, request: &ReadRequest) -> CommResult<DeviceValue> {
        let _guard = self.gate.lock().await;
        let address = McAddress::from_str(&request.address)?;
        let bit = request.data_type == DeviceDataType::Bool;
        if bit && !address.bit_device {
            return Err(configuration("Bool reads require an MC bit device"));
        }
        let points = if bit {
            request.count
        } else {
            word_width(request.data_type)?
                .checked_mul(request.count)
                .ok_or_else(|| configuration("read count overflow"))?
        };
        let packet = match self.frame {
            Mc3eFrame::Binary => build_binary_batch(address, points, None, bit, self.timer)?,
            Mc3eFrame::Ascii => build_ascii_batch(address, points, None, bit, self.timer)?,
        };
        let response = self.exchange(&packet, request.timeout).await?;
        let data = match self.frame {
            Mc3eFrame::Binary => parse_binary_response(&response)?,
            Mc3eFrame::Ascii => parse_ascii_response(&response)?,
        };
        if bit {
            let values = (0..request.count)
                .map(|i| {
                    let byte = data[usize::from(i / 2)];
                    if i % 2 == 0 {
                        byte & 0xf0 != 0
                    } else {
                        byte & 0x0f != 0
                    }
                })
                .collect::<Vec<_>>();
            Ok(if request.count == 1 {
                DeviceValue::Bool(values[0])
            } else {
                DeviceValue::Bools(values)
            })
        } else {
            decode_words(&data, request.data_type, request.count)
        }
    }
    async fn write(&self, request: &WriteRequest) -> CommResult<()> {
        let _guard = self.gate.lock().await;
        let address = McAddress::from_str(&request.address)?;
        let (payload, points, bit) = encode_words(&request.value)?;
        if bit && !address.bit_device {
            return Err(configuration("Bool writes require an MC bit device"));
        }
        let packet = match self.frame {
            Mc3eFrame::Binary => {
                build_binary_batch(address, points, Some(&payload), bit, self.timer)?
            }
            Mc3eFrame::Ascii => {
                build_ascii_batch(address, points, Some(&payload), bit, self.timer)?
            }
        };
        let response = self.exchange(&packet, request.timeout).await?;
        match self.frame {
            Mc3eFrame::Binary => {
                parse_binary_response(&response)?;
            }
            Mc3eFrame::Ascii => {
                parse_ascii_response(&response)?;
            }
        }
        Ok(())
    }
    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}

fn word_width(kind: DeviceDataType) -> CommResult<u16> {
    Ok(match kind {
        DeviceDataType::UInt16 | DeviceDataType::Int16 => 1,
        DeviceDataType::UInt32 | DeviceDataType::Int32 | DeviceDataType::Float32 => 2,
        DeviceDataType::UInt64 | DeviceDataType::Int64 | DeviceDataType::Float64 => 4,
        DeviceDataType::String | DeviceDataType::Bytes => 1,
        DeviceDataType::Bool => return Err(configuration("Bool uses bit access")),
        _ => return Err(configuration("unsupported future MC data type")),
    })
}
fn decode_words(data: &[u8], kind: DeviceDataType, count: u16) -> CommResult<DeviceValue> {
    let needed = usize::from(word_width(kind)?) * 2 * usize::from(count);
    if data.len() < needed {
        return Err(protocol("MC payload is shorter than requested"));
    }
    macro_rules! values {
        ($n:expr,$conv:expr,$one:ident,$many:ident) => {{
            let v = data[..needed]
                .chunks_exact($n)
                .map($conv)
                .collect::<Vec<_>>();
            if count == 1 {
                DeviceValue::$one(v[0])
            } else {
                DeviceValue::$many(v)
            }
        }};
    }
    Ok(match kind {
        DeviceDataType::UInt16 => values!(
            2,
            |c: &[u8]| u16::from_le_bytes(c.try_into().unwrap()),
            UInt16,
            UInt16s
        ),
        DeviceDataType::Int16 => values!(
            2,
            |c: &[u8]| i16::from_le_bytes(c.try_into().unwrap()),
            Int16,
            Int16s
        ),
        DeviceDataType::UInt32 => values!(
            4,
            |c: &[u8]| u32::from_le_bytes(c.try_into().unwrap()),
            UInt32,
            UInt32s
        ),
        DeviceDataType::Int32 => values!(
            4,
            |c: &[u8]| i32::from_le_bytes(c.try_into().unwrap()),
            Int32,
            Int32s
        ),
        DeviceDataType::Float32 => values!(
            4,
            |c: &[u8]| f32::from_le_bytes(c.try_into().unwrap()),
            Float32,
            Float32s
        ),
        DeviceDataType::UInt64 => values!(
            8,
            |c: &[u8]| u64::from_le_bytes(c.try_into().unwrap()),
            UInt64,
            UInt64s
        ),
        DeviceDataType::Int64 => values!(
            8,
            |c: &[u8]| i64::from_le_bytes(c.try_into().unwrap()),
            Int64,
            Int64s
        ),
        DeviceDataType::Float64 => values!(
            8,
            |c: &[u8]| f64::from_le_bytes(c.try_into().unwrap()),
            Float64,
            Float64s
        ),
        DeviceDataType::String => DeviceValue::String(
            String::from_utf8_lossy(&data[..needed])
                .trim_end_matches('\0')
                .to_owned(),
        ),
        DeviceDataType::Bytes => DeviceValue::Bytes(data[..needed].to_vec()),
        _ => return Err(configuration("unsupported MC decode type")),
    })
}
fn encode_words(value: &DeviceValue) -> CommResult<(Vec<u8>, u16, bool)> {
    macro_rules! scalar {
        ($v:expr) => {{
            let b = $v.to_le_bytes().to_vec();
            let p = (b.len() / 2) as u16;
            Ok((b, p, false))
        }};
    }
    macro_rules! many {
        ($v:expr) => {{
            let mut b = Vec::new();
            for x in $v {
                b.extend_from_slice(&x.to_le_bytes());
            }
            let p = (b.len() / 2) as u16;
            Ok((b, p, false))
        }};
    }
    match value {
        DeviceValue::Bool(v) => Ok((vec![if *v { 0x10 } else { 0 }], 1, true)),
        DeviceValue::Bools(v) => {
            let mut b = vec![0; v.len().div_ceil(2)];
            for (i, x) in v.iter().enumerate() {
                if *x {
                    b[i / 2] |= if i % 2 == 0 { 0x10 } else { 1 };
                }
            }
            Ok((b, v.len() as u16, true))
        }
        DeviceValue::UInt16(v) => scalar!(v),
        DeviceValue::Int16(v) => scalar!(v),
        DeviceValue::UInt32(v) => scalar!(v),
        DeviceValue::Int32(v) => scalar!(v),
        DeviceValue::UInt64(v) => scalar!(v),
        DeviceValue::Int64(v) => scalar!(v),
        DeviceValue::Float32(v) => scalar!(v),
        DeviceValue::Float64(v) => scalar!(v),
        DeviceValue::UInt16s(v) => many!(v),
        DeviceValue::Int16s(v) => many!(v),
        DeviceValue::UInt32s(v) => many!(v),
        DeviceValue::Int32s(v) => many!(v),
        DeviceValue::UInt64s(v) => many!(v),
        DeviceValue::Int64s(v) => many!(v),
        DeviceValue::Float32s(v) => many!(v),
        DeviceValue::Float64s(v) => many!(v),
        DeviceValue::String(v) => {
            let mut b = v.as_bytes().to_vec();
            if b.len() % 2 != 0 {
                b.push(0);
            }
            let p = (b.len() / 2) as u16;
            Ok((b, p, false))
        }
        DeviceValue::Bytes(v) => {
            let mut b = v.clone();
            if b.len() % 2 != 0 {
                b.push(0);
            }
            let p = (b.len() / 2) as u16;
            Ok((b, p, false))
        }
        _ => Err(configuration("unsupported future MC value type")),
    }
}
fn protocol(message: impl Into<String>) -> CommError {
    CommError::new(
        "MC3E.PROTOCOL.INVALID",
        CommErrorCategory::Protocol,
        message,
        false,
    )
}
fn configuration(message: impl Into<String>) -> CommError {
    CommError::new(
        "MC3E.CONFIGURATION.INVALID",
        CommErrorCategory::Configuration,
        message,
        false,
    )
}
