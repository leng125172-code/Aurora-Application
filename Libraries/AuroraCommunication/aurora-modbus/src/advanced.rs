//! Less common Modbus public-function PDU builders.

use aurora_comm_core::{CommError, CommErrorCategory, CommResult};

/// One FC20 file-record read sub-request.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FileRecordRead {
    /// File number, in the range 1..=65535.
    pub file: u16,
    /// Record number within the file.
    pub record: u16,
    /// Number of 16-bit registers to read.
    pub registers: u16,
}

/// One FC21 file-record write sub-request.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FileRecordWrite {
    /// File number, in the range 1..=65535.
    pub file: u16,
    /// Record number within the file.
    pub record: u16,
    /// Register values to write.
    pub values: Vec<u16>,
}

/// Builds an FC22 mask-write-register PDU.
pub fn mask_write_register_pdu(address: u16, and_mask: u16, or_mask: u16) -> Vec<u8> {
    let mut pdu = vec![22];
    pdu.extend_from_slice(&address.to_be_bytes());
    pdu.extend_from_slice(&and_mask.to_be_bytes());
    pdu.extend_from_slice(&or_mask.to_be_bytes());
    pdu
}

/// Builds an FC23 read/write-multiple-registers PDU.
pub fn read_write_multiple_registers_pdu(
    read_address: u16,
    read_registers: u16,
    write_address: u16,
    values: &[u16],
) -> CommResult<Vec<u8>> {
    if read_registers == 0 || read_registers > 125 || values.is_empty() || values.len() > 121 {
        return Err(configuration("FC23 quantities exceed Modbus limits"));
    }
    let write_registers =
        u16::try_from(values.len()).map_err(|_| configuration("too many values"))?;
    let byte_count =
        u8::try_from(values.len() * 2).map_err(|_| configuration("too many values"))?;
    let mut pdu = vec![23];
    pdu.extend_from_slice(&read_address.to_be_bytes());
    pdu.extend_from_slice(&read_registers.to_be_bytes());
    pdu.extend_from_slice(&write_address.to_be_bytes());
    pdu.extend_from_slice(&write_registers.to_be_bytes());
    pdu.push(byte_count);
    for value in values {
        pdu.extend_from_slice(&value.to_be_bytes());
    }
    Ok(pdu)
}

/// Builds an FC20 read-file-record PDU.
pub fn read_file_record_pdu(records: &[FileRecordRead]) -> CommResult<Vec<u8>> {
    if records.is_empty() || records.len() > 35 {
        return Err(configuration("FC20 requires between 1 and 35 records"));
    }
    let byte_count =
        u8::try_from(records.len() * 7).map_err(|_| configuration("too many records"))?;
    let mut pdu = vec![20, byte_count];
    for record in records {
        if record.file == 0 || record.registers == 0 || record.registers > 125 {
            return Err(configuration("invalid FC20 file or register count"));
        }
        pdu.push(6);
        pdu.extend_from_slice(&record.file.to_be_bytes());
        pdu.extend_from_slice(&record.record.to_be_bytes());
        pdu.extend_from_slice(&record.registers.to_be_bytes());
    }
    Ok(pdu)
}

/// Builds an FC21 write-file-record PDU.
pub fn write_file_record_pdu(records: &[FileRecordWrite]) -> CommResult<Vec<u8>> {
    if records.is_empty() {
        return Err(configuration("FC21 requires at least one record"));
    }
    let payload_size = records.iter().try_fold(0_usize, |size, record| {
        if record.file == 0 || record.values.is_empty() || record.values.len() > 122 {
            Err(configuration("invalid FC21 file or register count"))
        } else {
            size.checked_add(7 + record.values.len() * 2)
                .ok_or_else(|| configuration("FC21 payload overflow"))
        }
    })?;
    let byte_count =
        u8::try_from(payload_size).map_err(|_| configuration("FC21 payload exceeds 255 bytes"))?;
    let mut pdu = vec![21, byte_count];
    for record in records {
        pdu.push(6);
        pdu.extend_from_slice(&record.file.to_be_bytes());
        pdu.extend_from_slice(&record.record.to_be_bytes());
        pdu.extend_from_slice(&(record.values.len() as u16).to_be_bytes());
        for value in &record.values {
            pdu.extend_from_slice(&value.to_be_bytes());
        }
    }
    Ok(pdu)
}

fn configuration(message: impl Into<String>) -> CommError {
    CommError::new(
        "MODBUS.REQUEST.INVALID",
        CommErrorCategory::Configuration,
        message,
        false,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn advanced_function_vectors_match_specification() {
        assert_eq!(
            mask_write_register_pdu(4, 0x00f2, 0x0025),
            [22, 0, 4, 0, 0xf2, 0, 0x25]
        );
        assert_eq!(
            read_write_multiple_registers_pdu(3, 6, 14, &[0x1234, 0x5678]).unwrap(),
            [23, 0, 3, 0, 6, 0, 14, 0, 2, 4, 0x12, 0x34, 0x56, 0x78]
        );
        assert_eq!(
            read_file_record_pdu(&[FileRecordRead {
                file: 4,
                record: 1,
                registers: 2
            }])
            .unwrap(),
            [20, 7, 6, 0, 4, 0, 1, 0, 2]
        );
    }
}
