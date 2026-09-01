//! Managed S7 client and PLC-family defaults.

use crate::{
    S7Address, build_cotp_connect, build_read_request, build_setup_communication,
    build_write_request, parse_read_response, parse_write_response,
};
use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DeviceClient, DeviceDataType,
    DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use aurora_comm_transport::{ByteStream, TcpTransport, TcpTransportOptions};
use std::str::FromStr;
use std::sync::atomic::{AtomicU16, Ordering};
use std::time::{Duration, SystemTime};
use tokio::sync::{Mutex, watch};

/// PLC families supported by the C# compatibility surface.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum S7Model {
    /// S7-1200.
    S1200,
    /// S7-300.
    S300,
    /// S7-400.
    S400,
    /// S7-1500.
    S1500,
    /// S7-200 SMART Ethernet CPU.
    S200Smart,
    /// S7-200 Ethernet/CP endpoint.
    S200,
}

impl S7Model {
    const fn default_remote_tsap(self) -> u16 {
        match self {
            Self::S300 => 0x0102,
            Self::S400 => 0x0103,
            Self::S200Smart => 0x0300,
            Self::S200 => 0x4d57,
            Self::S1200 | Self::S1500 => 0x0100,
        }
    }
}

/// Connection and negotiated-PDU settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct S7Options {
    /// PLC host name or IP address.
    pub host: String,
    /// ISO-on-TCP port, normally 102.
    pub port: u16,
    /// PLC family used for compatible TSAP defaults.
    pub model: S7Model,
    /// Calling TSAP.
    pub local_tsap: u16,
    /// Called TSAP; overrides the family default when present.
    pub remote_tsap: Option<u16>,
    /// Requested S7 PDU length.
    pub requested_pdu_length: u16,
    /// Connection timeout.
    pub connect_timeout: Duration,
}

impl S7Options {
    /// Creates family-specific defaults.
    pub fn new(host: impl Into<String>, model: S7Model) -> Self {
        Self {
            host: host.into(),
            port: 102,
            model,
            local_tsap: 0x0100,
            remote_tsap: None,
            requested_pdu_length: 480,
            connect_timeout: Duration::from_secs(5),
        }
    }
}

/// Serialized ISO-on-TCP/S7comm client.
pub struct S7Client {
    transport: TcpTransport,
    local_tsap: u16,
    remote_tsap: u16,
    requested_pdu: u16,
    reference: AtomicU16,
    gate: Mutex<()>,
    status_tx: watch::Sender<DeviceStatus>,
}

impl S7Client {
    /// Creates a disconnected client.
    pub fn new(options: S7Options) -> Self {
        let mut transport = TcpTransportOptions::new(options.host, options.port);
        transport.connect_timeout = options.connect_timeout;
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Self {
            transport: TcpTransport::new(transport),
            local_tsap: options.local_tsap,
            remote_tsap: options
                .remote_tsap
                .unwrap_or_else(|| options.model.default_remote_tsap()),
            requested_pdu: options.requested_pdu_length,
            reference: AtomicU16::new(1),
            gate: Mutex::new(()),
            status_tx,
        }
    }

    async fn packet(&self, request: &[u8], timeout: Duration) -> CommResult<Vec<u8>> {
        self.transport.write_all(request, timeout).await?;
        let header = self.transport.read_exact(4, timeout).await?;
        if header[0..2] != [3, 0] {
            return Err(protocol("invalid TPKT header"));
        }
        let length = usize::from(u16::from_be_bytes([header[2], header[3]]));
        if length < 4 || length > usize::from(self.requested_pdu) + 64 {
            return Err(protocol("invalid TPKT length"));
        }
        let mut frame = header;
        frame.extend(self.transport.read_exact(length - 4, timeout).await?);
        Ok(frame)
    }

    fn publish(&self, state: ConnectionState, error: Option<&CommError>) {
        self.status_tx.send_replace(DeviceStatus {
            state,
            changed_at: SystemTime::now(),
            last_error_code: error.map(|value| value.code.clone()),
            reconnect_attempt: 0,
        });
    }
}

#[async_trait]
impl DeviceClient for S7Client {
    async fn connect(&self) -> CommResult<()> {
        let _guard = self.gate.lock().await;
        self.publish(ConnectionState::Connecting, None);
        let result = async {
            self.transport.connect().await?;
            let cotp = self
                .packet(
                    &build_cotp_connect(self.local_tsap, self.remote_tsap),
                    Duration::from_secs(5),
                )
                .await?;
            if cotp.get(5).copied() != Some(0xd0) {
                return Err(protocol("COTP connection was not confirmed"));
            }
            let reference = self.reference.fetch_add(1, Ordering::Relaxed);
            let setup = self
                .packet(
                    &build_setup_communication(reference, self.requested_pdu),
                    Duration::from_secs(5),
                )
                .await?;
            if setup.get(7).copied() != Some(0x32) {
                return Err(protocol("S7 setup communication failed"));
            }
            Ok(())
        }
        .await;
        match result {
            Ok(()) => {
                self.publish(ConnectionState::Connected, None);
                Ok(())
            }
            Err(error) => {
                let _ = self.transport.disconnect().await;
                self.publish(ConnectionState::Faulted, Some(&error));
                Err(error)
            }
        }
    }

    async fn disconnect(&self) -> CommResult<()> {
        let result = self.transport.disconnect().await;
        self.publish(ConnectionState::Disconnected, result.as_ref().err());
        result
    }

    async fn read(&self, request: &ReadRequest) -> CommResult<DeviceValue> {
        let _guard = self.gate.lock().await;
        let address = S7Address::from_str(&request.address)?;
        let (byte_width, bit) = type_width(request.data_type)?;
        let count = if bit {
            request.count
        } else {
            byte_width
                .checked_mul(request.count)
                .ok_or_else(|| protocol("read length overflow"))?
        };
        let reference = self.reference.fetch_add(1, Ordering::Relaxed);
        let response = self
            .packet(
                &build_read_request(reference, address, count, bit)?,
                request.timeout,
            )
            .await?;
        decode_value(
            &parse_read_response(&response)?,
            request.data_type,
            request.count,
        )
    }

    async fn write(&self, request: &WriteRequest) -> CommResult<()> {
        let _guard = self.gate.lock().await;
        let address = S7Address::from_str(&request.address)?;
        let (payload, count, bit) = encode_value(&request.value)?;
        let reference = self.reference.fetch_add(1, Ordering::Relaxed);
        let response = self
            .packet(
                &build_write_request(reference, address, count, bit, &payload)?,
                request.timeout,
            )
            .await?;
        parse_write_response(&response)
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}

fn type_width(data_type: DeviceDataType) -> CommResult<(u16, bool)> {
    Ok(match data_type {
        DeviceDataType::Bool => (1, true),
        DeviceDataType::UInt16 | DeviceDataType::Int16 => (2, false),
        DeviceDataType::UInt32 | DeviceDataType::Int32 | DeviceDataType::Float32 => (4, false),
        DeviceDataType::UInt64 | DeviceDataType::Int64 | DeviceDataType::Float64 => (8, false),
        DeviceDataType::String | DeviceDataType::Bytes => (1, false),
        _ => return Err(protocol("unsupported future S7 data type")),
    })
}

fn decode_value(bytes: &[u8], kind: DeviceDataType, count: u16) -> CommResult<DeviceValue> {
    let required = usize::from(type_width(kind)?.0) * usize::from(count);
    if bytes.len() < required {
        return Err(protocol("S7 response payload is shorter than requested"));
    }
    macro_rules! values {
        ($size:expr, $convert:expr, $single:ident, $many:ident) => {{
            let values = bytes[..required]
                .chunks_exact($size)
                .map($convert)
                .collect::<Vec<_>>();
            if count == 1 {
                DeviceValue::$single(values[0])
            } else {
                DeviceValue::$many(values)
            }
        }};
    }
    Ok(match kind {
        DeviceDataType::Bool => {
            if count == 1 {
                DeviceValue::Bool(bytes[0] & 1 != 0)
            } else {
                DeviceValue::Bools(
                    (0..count)
                        .map(|i| bytes[usize::from(i / 8)] & (1 << (i % 8)) != 0)
                        .collect(),
                )
            }
        }
        DeviceDataType::UInt16 => values!(
            2,
            |c: &[u8]| u16::from_be_bytes([c[0], c[1]]),
            UInt16,
            UInt16s
        ),
        DeviceDataType::Int16 => values!(
            2,
            |c: &[u8]| i16::from_be_bytes([c[0], c[1]]),
            Int16,
            Int16s
        ),
        DeviceDataType::UInt32 => values!(
            4,
            |c: &[u8]| u32::from_be_bytes(c.try_into().unwrap()),
            UInt32,
            UInt32s
        ),
        DeviceDataType::Int32 => values!(
            4,
            |c: &[u8]| i32::from_be_bytes(c.try_into().unwrap()),
            Int32,
            Int32s
        ),
        DeviceDataType::UInt64 => values!(
            8,
            |c: &[u8]| u64::from_be_bytes(c.try_into().unwrap()),
            UInt64,
            UInt64s
        ),
        DeviceDataType::Int64 => values!(
            8,
            |c: &[u8]| i64::from_be_bytes(c.try_into().unwrap()),
            Int64,
            Int64s
        ),
        DeviceDataType::Float32 => values!(
            4,
            |c: &[u8]| f32::from_be_bytes(c.try_into().unwrap()),
            Float32,
            Float32s
        ),
        DeviceDataType::Float64 => values!(
            8,
            |c: &[u8]| f64::from_be_bytes(c.try_into().unwrap()),
            Float64,
            Float64s
        ),
        DeviceDataType::String => DeviceValue::String(
            String::from_utf8_lossy(&bytes[..required])
                .trim_end_matches('\0')
                .to_owned(),
        ),
        DeviceDataType::Bytes => DeviceValue::Bytes(bytes[..required].to_vec()),
        _ => return Err(protocol("unsupported future S7 data type")),
    })
}

fn encode_value(value: &DeviceValue) -> CommResult<(Vec<u8>, u16, bool)> {
    macro_rules! scalar {
        ($v:expr) => {
            Ok((
                $v.to_be_bytes().to_vec(),
                wire_count(std::mem::size_of_val($v))?,
                false,
            ))
        };
    }
    macro_rules! many {
        ($v:expr) => {{
            let mut b = Vec::new();
            for v in $v {
                b.extend_from_slice(&v.to_be_bytes());
            }
            let length = std::mem::size_of_val($v.as_slice());
            Ok((b, wire_count(length)?, false))
        }};
    }
    match value {
        DeviceValue::Bool(v) => Ok((vec![u8::from(*v)], 1, true)),
        DeviceValue::Bools(v) => {
            let mut b = vec![0; v.len().div_ceil(8)];
            for (i, x) in v.iter().enumerate() {
                if *x {
                    b[i / 8] |= 1 << (i % 8);
                }
            }
            Ok((b, wire_count(v.len())?, true))
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
        DeviceValue::String(v) => Ok((v.as_bytes().to_vec(), wire_count(v.len())?, false)),
        DeviceValue::Bytes(v) => Ok((v.clone(), wire_count(v.len())?, false)),
        _ => Err(protocol("unsupported future S7 value type")),
    }
}

fn wire_count(value: usize) -> CommResult<u16> {
    u16::try_from(value).map_err(|_| protocol("S7 value exceeds 65535 wire units"))
}

fn protocol(message: impl Into<String>) -> CommError {
    CommError::new(
        "S7.PROTOCOL.INVALID",
        CommErrorCategory::Protocol,
        message,
        false,
    )
}
