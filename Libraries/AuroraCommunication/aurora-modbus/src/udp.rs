//! Modbus UDP client using MBAP transaction identifiers.

use crate::address::ModbusAddress;
use crate::codec::{encode_write, read_pdu};
use crate::stream::{decode_read, read_quantity, validate_write_echo};
use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DataLayout, DeviceClient,
    DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use aurora_comm_transport::{DatagramTransport, UdpTransport, UdpTransportOptions};
use std::str::FromStr;
use std::sync::atomic::{AtomicU16, Ordering};
use std::time::{Duration, SystemTime};
use tokio::sync::watch;

/// Modbus UDP connection and encoding options.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModbusUdpOptions {
    /// UDP endpoint options.
    pub transport: UdpTransportOptions,
    /// Default unit identifier.
    pub unit_id: u8,
    /// Whether addresses are already zero-based.
    pub address_start_with_zero: bool,
    /// Multi-register byte and word ordering.
    pub data_layout: DataLayout,
    /// Forces FC16 for single-register writes.
    pub disable_function_code_06: bool,
}

impl ModbusUdpOptions {
    /// Creates options using the standard Modbus port.
    pub fn new(host: impl Into<String>, unit_id: u8) -> Self {
        Self {
            transport: UdpTransportOptions::new(host, 502),
            unit_id,
            address_start_with_zero: true,
            data_layout: DataLayout::Abcd,
            disable_function_code_06: false,
        }
    }
}

/// Thread-safe Modbus UDP client.
pub struct ModbusUdpClient {
    options: ModbusUdpOptions,
    transport: UdpTransport,
    transaction_id: AtomicU16,
    status_tx: watch::Sender<DeviceStatus>,
}

impl ModbusUdpClient {
    /// Creates a disconnected UDP client.
    pub fn new(options: ModbusUdpOptions) -> Self {
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        let transport = UdpTransport::new(options.transport.clone());
        Self {
            options,
            transport,
            transaction_id: AtomicU16::new(0),
            status_tx,
        }
    }

    async fn transact(&self, unit: u8, pdu: &[u8], timeout: Duration) -> CommResult<Vec<u8>> {
        let transaction = self.transaction_id.fetch_add(1, Ordering::Relaxed);
        let length = u16::try_from(pdu.len() + 1).map_err(|_| {
            CommError::new(
                "MODBUS.REQUEST.TOO_LARGE",
                CommErrorCategory::Configuration,
                "Modbus UDP request is too large",
                false,
            )
        })?;
        let mut frame = Vec::with_capacity(7 + pdu.len());
        frame.extend(transaction.to_be_bytes());
        frame.extend(0_u16.to_be_bytes());
        frame.extend(length.to_be_bytes());
        frame.push(unit);
        frame.extend(pdu);
        let response = self
            .transport
            .request_response(&frame, 2048, timeout)
            .await?;
        if response.len() < 8
            || u16::from_be_bytes([response[0], response[1]]) != transaction
            || response[2..4] != [0, 0]
            || response[6] != unit
        {
            return Err(CommError::new(
                "MODBUS.RESPONSE.MBAP",
                CommErrorCategory::Protocol,
                "Modbus UDP MBAP header is invalid",
                false,
            ));
        }
        let declared = usize::from(u16::from_be_bytes([response[4], response[5]]));
        if declared + 6 != response.len() {
            return Err(CommError::new(
                "MODBUS.RESPONSE.LENGTH",
                CommErrorCategory::Protocol,
                "Modbus UDP response length is invalid",
                false,
            ));
        }
        let response_pdu = response[7..].to_vec();
        if response_pdu[0] == pdu[0] | 0x80 {
            let exception = response_pdu.get(1).copied().unwrap_or_default();
            return Err(CommError::new(
                "MODBUS.RESPONSE.EXCEPTION",
                CommErrorCategory::DeviceRejected,
                format!("Modbus exception response {exception}"),
                false,
            )
            .with_protocol_code(i32::from(exception)));
        }
        if response_pdu[0] != pdu[0] {
            return Err(CommError::new(
                "MODBUS.RESPONSE.FUNCTION",
                CommErrorCategory::Protocol,
                "Modbus UDP response function does not match request",
                false,
            ));
        }
        Ok(response_pdu)
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
impl DeviceClient for ModbusUdpClient {
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
        let pdu = self
            .transact(
                unit,
                &read_pdu(address.function, address.offset, quantity),
                request.timeout,
            )
            .await?;
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
        let response = self.transact(unit, &request_pdu, request.timeout).await?;
        validate_write_echo(&request_pdu, &response)
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}
