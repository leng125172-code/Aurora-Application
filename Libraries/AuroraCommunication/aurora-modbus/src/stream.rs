//! Modbus RTU/ASCII over serial or TCP byte streams.

use crate::address::ModbusAddress;
use crate::codec::{decode_bits, decode_registers, encode_write, read_pdu, register_width};
use crate::framing::{decode_ascii_frame, decode_rtu_frame, encode_ascii_frame, encode_rtu_frame};
use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DataLayout, DeviceClient,
    DeviceDataType, DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use aurora_comm_transport::{
    ByteStream, SerialTransport, SerialTransportOptions, TcpTransport, TcpTransportOptions,
};
use std::str::FromStr;
use std::sync::Arc;
use std::time::{Duration, SystemTime};
use tokio::sync::{Mutex, watch};

/// Stream framing used for a serial port or TCP tunnel.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ModbusStreamMode {
    /// Binary RTU framing with CRC16.
    Rtu,
    /// Colon-delimited hexadecimal ASCII framing with LRC.
    Ascii,
}

/// Protocol behavior shared by serial and tunneled stream clients.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModbusStreamOptions {
    /// Default slave/unit identifier.
    pub unit_id: u8,
    /// Frame representation.
    pub mode: ModbusStreamMode,
    /// Whether addresses are already zero-based.
    pub address_start_with_zero: bool,
    /// Multi-register byte and word ordering.
    pub data_layout: DataLayout,
    /// Forces FC16 for single-register writes.
    pub disable_function_code_06: bool,
    /// Unit id that receives write broadcasts without a response.
    pub broadcast_unit: Option<u8>,
    /// Maximum accepted ASCII response size.
    pub maximum_frame_size: usize,
}

impl ModbusStreamOptions {
    /// Creates RTU options for one slave.
    pub const fn rtu(unit_id: u8) -> Self {
        Self {
            unit_id,
            mode: ModbusStreamMode::Rtu,
            address_start_with_zero: true,
            data_layout: DataLayout::Abcd,
            disable_function_code_06: false,
            broadcast_unit: Some(0),
            maximum_frame_size: 1024,
        }
    }

    /// Creates ASCII options for one slave.
    pub const fn ascii(unit_id: u8) -> Self {
        Self {
            mode: ModbusStreamMode::Ascii,
            ..Self::rtu(unit_id)
        }
    }
}

/// A serialized Modbus stream client usable with serial ports and TCP gateways.
pub struct ModbusStreamClient {
    options: ModbusStreamOptions,
    transport: Arc<dyn ByteStream>,
    gate: Mutex<()>,
    status_tx: watch::Sender<DeviceStatus>,
}

impl ModbusStreamClient {
    /// Creates a client around a custom byte stream, including test transports.
    pub fn new(options: ModbusStreamOptions, transport: Arc<dyn ByteStream>) -> Self {
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Self {
            options,
            transport,
            gate: Mutex::new(()),
            status_tx,
        }
    }

    /// Creates standard Modbus RTU over a serial port.
    pub fn rtu_serial(unit_id: u8, transport: SerialTransportOptions) -> Self {
        Self::new(
            ModbusStreamOptions::rtu(unit_id),
            Arc::new(SerialTransport::new(transport)),
        )
    }

    /// Creates Modbus ASCII over a serial port.
    pub fn ascii_serial(unit_id: u8, transport: SerialTransportOptions) -> Self {
        Self::new(
            ModbusStreamOptions::ascii(unit_id),
            Arc::new(SerialTransport::new(transport)),
        )
    }

    /// Creates Modbus RTU framing carried by a TCP stream.
    pub fn rtu_over_tcp(unit_id: u8, transport: TcpTransportOptions) -> Self {
        Self::new(
            ModbusStreamOptions::rtu(unit_id),
            Arc::new(TcpTransport::new(transport)),
        )
    }

    /// Creates Modbus ASCII framing carried by a TCP stream.
    pub fn ascii_over_tcp(unit_id: u8, transport: TcpTransportOptions) -> Self {
        Self::new(
            ModbusStreamOptions::ascii(unit_id),
            Arc::new(TcpTransport::new(transport)),
        )
    }

    async fn transact(
        &self,
        unit: u8,
        pdu: &[u8],
        deadline: Duration,
        expect_response: bool,
    ) -> CommResult<Option<Vec<u8>>> {
        let _guard = self.gate.lock().await;
        let frame = match self.options.mode {
            ModbusStreamMode::Rtu => encode_rtu_frame(unit, pdu),
            ModbusStreamMode::Ascii => encode_ascii_frame(unit, pdu),
        };
        self.transport.write_all(&frame, deadline).await?;
        if !expect_response {
            return Ok(None);
        }
        let response = match self.options.mode {
            ModbusStreamMode::Rtu => self.read_rtu_response(pdu[0], deadline).await?,
            ModbusStreamMode::Ascii => {
                self.transport
                    .read_until(b'\n', self.options.maximum_frame_size, deadline)
                    .await?
            }
        };
        let (response_unit, response_pdu) = match self.options.mode {
            ModbusStreamMode::Rtu => decode_rtu_frame(&response)?,
            ModbusStreamMode::Ascii => decode_ascii_frame(&response)?,
        };
        validate_response(unit, pdu[0], response_unit, &response_pdu)?;
        Ok(Some(response_pdu))
    }

    async fn read_rtu_response(&self, function: u8, deadline: Duration) -> CommResult<Vec<u8>> {
        let mut frame = self.transport.read_exact(2, deadline).await?;
        let response_function = frame[1];
        if response_function & 0x80 != 0 {
            frame.extend(self.transport.read_exact(3, deadline).await?);
            return Ok(frame);
        }
        match function {
            1..=4 | 20 | 23 => {
                let count = self.transport.read_exact(1, deadline).await?;
                let byte_count = usize::from(count[0]);
                frame.extend(count);
                frame.extend(self.transport.read_exact(byte_count + 2, deadline).await?);
            }
            5 | 6 | 15 | 16 | 22 => {
                frame.extend(self.transport.read_exact(6, deadline).await?);
            }
            _ => {
                return Err(CommError::new(
                    "MODBUS.RTU.FUNCTION_UNSUPPORTED",
                    CommErrorCategory::Configuration,
                    format!("Cannot determine RTU response length for function {function}"),
                    false,
                ));
            }
        }
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
impl DeviceClient for ModbusStreamClient {
    async fn connect(&self) -> CommResult<()> {
        self.publish(ConnectionState::Connecting, None);
        match self.transport.connect().await {
            Ok(()) => {
                self.publish(ConnectionState::Connected, None);
                Ok(())
            }
            Err(error) => {
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
        let mut address = ModbusAddress::from_str(&request.address)?;
        address.apply_base(self.options.address_start_with_zero)?;
        let unit = address.station.unwrap_or(self.options.unit_id);
        let quantity = read_quantity(address.function, request.data_type, request.count)?;
        let request_pdu = read_pdu(address.function, address.offset, quantity);
        let pdu = self
            .transact(unit, &request_pdu, request.timeout, true)
            .await?
            .expect("read always expects a response");
        decode_read(&pdu, address.function, request, self.options.data_layout)
    }

    async fn write(&self, request: &WriteRequest) -> CommResult<()> {
        let mut address = ModbusAddress::from_str(&request.address)?;
        address.apply_base(self.options.address_start_with_zero)?;
        let unit = address.station.unwrap_or(self.options.unit_id);
        let request_pdu = encode_write(
            address.function,
            address.offset,
            &request.value,
            self.options.data_layout,
            self.options.disable_function_code_06,
        )?;
        let expect_response = self.options.broadcast_unit != Some(unit);
        let response = self
            .transact(unit, &request_pdu, request.timeout, expect_response)
            .await?;
        if let Some(pdu) = response {
            validate_write_echo(&request_pdu, &pdu)?;
        }
        Ok(())
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}

pub(crate) fn read_quantity(
    function: u8,
    data_type: DeviceDataType,
    count: u16,
) -> CommResult<u16> {
    if matches!(function, 1 | 2) {
        if data_type != DeviceDataType::Bool {
            return Err(CommError::new(
                "MODBUS.TYPE.BIT_AREA",
                CommErrorCategory::Configuration,
                "Modbus bit areas require Bool values",
                false,
            ));
        }
        Ok(count)
    } else {
        register_width(data_type)?
            .checked_mul(count)
            .ok_or_else(|| {
                CommError::new(
                    "MODBUS.READ.TOO_LARGE",
                    CommErrorCategory::Configuration,
                    "Modbus read quantity overflow",
                    false,
                )
            })
    }
}

pub(crate) fn decode_read(
    pdu: &[u8],
    function: u8,
    request: &ReadRequest,
    layout: DataLayout,
) -> CommResult<DeviceValue> {
    let byte_count = usize::from(*pdu.get(1).ok_or_else(|| {
        CommError::new(
            "MODBUS.RESPONSE.PAYLOAD",
            CommErrorCategory::Protocol,
            "Modbus read response has no byte count",
            false,
        )
    })?);
    let payload = pdu.get(2..).ok_or_else(|| {
        CommError::new(
            "MODBUS.RESPONSE.PAYLOAD",
            CommErrorCategory::Protocol,
            "Modbus read response has no payload",
            false,
        )
    })?;
    if payload.len() != byte_count {
        return Err(CommError::new(
            "MODBUS.RESPONSE.BYTE_COUNT",
            CommErrorCategory::Protocol,
            "Modbus byte count does not match payload",
            false,
        ));
    }
    if matches!(function, 1 | 2) {
        decode_bits(payload, request.count)
    } else {
        decode_registers(payload, request.data_type, request.count, layout)
    }
}

pub(crate) fn validate_write_echo(request: &[u8], response: &[u8]) -> CommResult<()> {
    if response.get(1..5) == request.get(1..5) {
        Ok(())
    } else {
        Err(CommError::new(
            "MODBUS.WRITE.ECHO",
            CommErrorCategory::Protocol,
            "Modbus write response did not echo address and quantity",
            false,
        ))
    }
}

fn validate_response(
    unit: u8,
    function: u8,
    response_unit: u8,
    response_pdu: &[u8],
) -> CommResult<()> {
    if response_unit != unit {
        return Err(CommError::new(
            "MODBUS.RESPONSE.UNIT",
            CommErrorCategory::Protocol,
            "Modbus response unit does not match request",
            false,
        ));
    }
    let response_function = response_pdu.first().copied().unwrap_or_default();
    if response_function == function | 0x80 {
        let exception = response_pdu.get(1).copied().unwrap_or_default();
        return Err(CommError::new(
            "MODBUS.RESPONSE.EXCEPTION",
            CommErrorCategory::DeviceRejected,
            format!("Modbus exception response {exception}"),
            false,
        )
        .with_protocol_code(i32::from(exception)));
    }
    if response_function != function {
        return Err(CommError::new(
            "MODBUS.RESPONSE.FUNCTION",
            CommErrorCategory::Protocol,
            "Modbus response function does not match request",
            false,
        ));
    }
    Ok(())
}
