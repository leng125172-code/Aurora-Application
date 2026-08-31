//! Managed Modbus TCP client.

use crate::address::ModbusAddress;
use crate::codec::{decode_bits, decode_registers, encode_write, read_pdu, register_width};
use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DataLayout, DeviceClient,
    DeviceDataType, DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use aurora_comm_transport::{ByteStream, TcpTransport, TcpTransportOptions};
use std::str::FromStr;
use std::sync::atomic::{AtomicU16, Ordering};
use std::time::{Duration, SystemTime};
use tokio::sync::{Mutex, watch};

/// Modbus TCP client options.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModbusTcpOptions {
    /// PLC DNS name or IP address.
    pub host: String,
    /// Modbus TCP port.
    pub port: u16,
    /// Default Modbus unit identifier.
    pub unit_id: u8,
    /// Maximum connection-establishment duration.
    pub connect_timeout: Duration,
    /// Whether addresses are already zero-based protocol offsets.
    pub address_start_with_zero: bool,
    /// Multi-register byte and word ordering.
    pub data_layout: DataLayout,
    /// Forces FC16 even for a single holding register.
    pub disable_function_code_06: bool,
}

impl ModbusTcpOptions {
    /// Creates options using the standard Modbus TCP port.
    pub fn new(host: impl Into<String>, unit_id: u8) -> Self {
        Self {
            host: host.into(),
            port: 502,
            unit_id,
            connect_timeout: Duration::from_secs(5),
            address_start_with_zero: true,
            data_layout: DataLayout::Abcd,
            disable_function_code_06: false,
        }
    }
}

/// A thread-safe Modbus TCP client. Transactions are serialized per instance.
pub struct ModbusTcpClient {
    unit_id: u8,
    transport: TcpTransport,
    transaction_id: AtomicU16,
    transaction_gate: Mutex<()>,
    status_tx: watch::Sender<DeviceStatus>,
    address_start_with_zero: bool,
    data_layout: DataLayout,
    disable_function_code_06: bool,
}

impl ModbusTcpClient {
    /// Creates a disconnected client.
    pub fn new(options: ModbusTcpOptions) -> Self {
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        let mut transport_options = TcpTransportOptions::new(options.host, options.port);
        transport_options.connect_timeout = options.connect_timeout;
        Self {
            unit_id: options.unit_id,
            transport: TcpTransport::new(transport_options),
            transaction_id: AtomicU16::new(0),
            transaction_gate: Mutex::new(()),
            status_tx,
            address_start_with_zero: options.address_start_with_zero,
            data_layout: options.data_layout,
            disable_function_code_06: options.disable_function_code_06,
        }
    }

    async fn transact(
        &self,
        unit_id: u8,
        request_pdu: &[u8],
        deadline: Duration,
    ) -> CommResult<Vec<u8>> {
        let _transaction = self.transaction_gate.lock().await;
        let transaction_id = self.transaction_id.fetch_add(1, Ordering::Relaxed);
        let length = u16::try_from(request_pdu.len() + 1).map_err(|_| {
            CommError::new(
                "MODBUS.REQUEST.TOO_LARGE",
                CommErrorCategory::Configuration,
                "Modbus request exceeds MBAP length limits",
                false,
            )
        })?;

        let mut frame = Vec::with_capacity(7 + request_pdu.len());
        frame.extend_from_slice(&transaction_id.to_be_bytes());
        frame.extend_from_slice(&0_u16.to_be_bytes());
        frame.extend_from_slice(&length.to_be_bytes());
        frame.push(unit_id);
        frame.extend_from_slice(request_pdu);

        self.transport.write_all(&frame, deadline).await?;
        let header = self.transport.read_exact(7, deadline).await?;
        let response_transaction = u16::from_be_bytes([header[0], header[1]]);
        let protocol = u16::from_be_bytes([header[2], header[3]]);
        let response_length = u16::from_be_bytes([header[4], header[5]]);
        if response_transaction != transaction_id || protocol != 0 || header[6] != unit_id {
            return Err(CommError::new(
                "MODBUS.RESPONSE.MBAP",
                CommErrorCategory::Protocol,
                "Modbus response MBAP header does not match the request",
                false,
            ));
        }
        if response_length < 2 {
            return Err(CommError::new(
                "MODBUS.RESPONSE.LENGTH",
                CommErrorCategory::Protocol,
                "Modbus response contains an invalid MBAP length",
                false,
            ));
        }
        let pdu = self
            .transport
            .read_exact(usize::from(response_length - 1), deadline)
            .await?;
        if pdu[0] & 0x80 != 0 {
            let exception = pdu.get(1).copied().unwrap_or_default();
            return Err(CommError::new(
                "MODBUS.RESPONSE.EXCEPTION",
                CommErrorCategory::DeviceRejected,
                format!("Modbus exception response {exception}"),
                false,
            )
            .with_protocol_code(i32::from(exception)));
        }
        if pdu[0] != request_pdu[0] {
            return Err(CommError::new(
                "MODBUS.RESPONSE.FUNCTION",
                CommErrorCategory::Protocol,
                "Modbus response function does not match the request",
                false,
            ));
        }
        Ok(pdu)
    }

    fn publish_state(&self, state: ConnectionState, error: Option<&CommError>) {
        self.status_tx.send_replace(DeviceStatus {
            state,
            changed_at: SystemTime::now(),
            last_error_code: error.map(|value| value.code.clone()),
            reconnect_attempt: 0,
        });
    }
}

#[async_trait]
impl DeviceClient for ModbusTcpClient {
    async fn connect(&self) -> CommResult<()> {
        self.publish_state(ConnectionState::Connecting, None);
        match self.transport.connect().await {
            Ok(()) => {
                self.publish_state(ConnectionState::Connected, None);
                Ok(())
            }
            Err(error) => {
                self.publish_state(ConnectionState::Faulted, Some(&error));
                Err(error)
            }
        }
    }

    async fn disconnect(&self) -> CommResult<()> {
        let result = self.transport.disconnect().await;
        self.publish_state(ConnectionState::Disconnected, result.as_ref().err());
        result
    }

    async fn read(&self, request: &ReadRequest) -> CommResult<DeviceValue> {
        let mut address = ModbusAddress::from_str(&request.address)?;
        address.apply_base(self.address_start_with_zero)?;
        let unit = address.station.unwrap_or(self.unit_id);
        let quantity = if matches!(address.function, 1 | 2) {
            if request.data_type != DeviceDataType::Bool {
                return Err(CommError::new(
                    "MODBUS.TYPE.BIT_AREA",
                    CommErrorCategory::Configuration,
                    "Modbus coil and discrete-input areas require Bool values",
                    false,
                ));
            }
            request.count
        } else {
            register_width(request.data_type)?
                .checked_mul(request.count)
                .ok_or_else(|| {
                    CommError::new(
                        "MODBUS.READ.TOO_LARGE",
                        CommErrorCategory::Configuration,
                        "Modbus read quantity overflow",
                        false,
                    )
                })?
        };

        let request_pdu = read_pdu(address.function, address.offset, quantity);
        let response = self.transact(unit, &request_pdu, request.timeout).await;
        let pdu = match response {
            Ok(value) => value,
            Err(error) => {
                if error.invalidates_connection() {
                    let _ = self.transport.disconnect().await;
                    self.publish_state(ConnectionState::Faulted, Some(&error));
                }
                return Err(error);
            }
        };
        let byte_count = usize::from(*pdu.get(1).ok_or_else(|| {
            CommError::new(
                "MODBUS.RESPONSE.PAYLOAD",
                CommErrorCategory::Protocol,
                "Modbus read response has no byte-count field",
                false,
            )
        })?);
        let payload = pdu.get(2..).ok_or_else(|| {
            CommError::new(
                "MODBUS.RESPONSE.PAYLOAD",
                CommErrorCategory::Protocol,
                "Modbus read response has no data payload",
                false,
            )
        })?;
        if byte_count != payload.len() {
            return Err(CommError::new(
                "MODBUS.RESPONSE.BYTE_COUNT",
                CommErrorCategory::Protocol,
                "Modbus byte count does not match the response payload",
                false,
            ));
        }

        if matches!(address.function, 1 | 2) {
            decode_bits(payload, request.count)
        } else {
            decode_registers(payload, request.data_type, request.count, self.data_layout)
        }
    }

    async fn write(&self, request: &WriteRequest) -> CommResult<()> {
        let mut address = ModbusAddress::from_str(&request.address)?;
        address.apply_base(self.address_start_with_zero)?;
        let unit = address.station.unwrap_or(self.unit_id);
        let request_pdu = encode_write(
            address.function,
            address.offset,
            &request.value,
            self.data_layout,
            self.disable_function_code_06,
        )?;
        let response = self.transact(unit, &request_pdu, request.timeout).await;
        match response {
            Ok(pdu) if pdu.get(1..5) == request_pdu.get(1..5) => Ok(()),
            Ok(_) => Err(CommError::new(
                "MODBUS.WRITE.ECHO",
                CommErrorCategory::Protocol,
                "Modbus write response did not echo the requested address and quantity",
                false,
            )),
            Err(error) => {
                if error.invalidates_connection() {
                    let _ = self.transport.disconnect().await;
                    self.publish_state(ConnectionState::Faulted, Some(&error));
                }
                Err(error)
            }
        }
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}
