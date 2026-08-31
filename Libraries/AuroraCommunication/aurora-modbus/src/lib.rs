//! Modbus protocol implementation for `AuroraCommunication`.

mod address;
mod advanced;
mod codec;
mod framing;
mod stream;
mod tcp;
mod udp;

pub use address::ModbusAddress;
pub use advanced::{
    FileRecordRead, FileRecordWrite, mask_write_register_pdu, read_file_record_pdu,
    read_write_multiple_registers_pdu, write_file_record_pdu,
};
pub use framing::{decode_ascii_frame, decode_rtu_frame, encode_ascii_frame, encode_rtu_frame};
pub use stream::{ModbusStreamClient, ModbusStreamMode, ModbusStreamOptions};
pub use tcp::{ModbusTcpClient, ModbusTcpOptions};
pub use udp::{ModbusUdpClient, ModbusUdpOptions};
