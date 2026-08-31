//! Modbus protocol implementation for `AuroraCommunication`.

mod address;
mod codec;
mod tcp;

pub use address::ModbusAddress;
pub use tcp::{ModbusTcpClient, ModbusTcpOptions};
