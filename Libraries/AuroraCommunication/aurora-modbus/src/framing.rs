//! Modbus RTU and ASCII framing with strict checksum validation.

use aurora_comm_core::{CommError, CommErrorCategory, CommResult};

/// Encodes one RTU application data unit.
pub fn encode_rtu_frame(unit_id: u8, pdu: &[u8]) -> Vec<u8> {
    let mut frame = Vec::with_capacity(pdu.len() + 3);
    frame.push(unit_id);
    frame.extend_from_slice(pdu);
    frame.extend_from_slice(&crc16(&frame).to_le_bytes());
    frame
}

/// Validates and decodes one complete RTU application data unit.
pub fn decode_rtu_frame(frame: &[u8]) -> CommResult<(u8, Vec<u8>)> {
    if frame.len() < 4 {
        return Err(frame_error(
            "MODBUS.RTU.LENGTH",
            "RTU frame is shorter than four bytes",
        ));
    }
    let payload_length = frame.len() - 2;
    let received = u16::from_le_bytes([frame[payload_length], frame[payload_length + 1]]);
    if crc16(&frame[..payload_length]) != received {
        return Err(frame_error("MODBUS.RTU.CRC", "RTU CRC16 validation failed"));
    }
    Ok((frame[0], frame[1..payload_length].to_vec()))
}

/// Encodes one Modbus ASCII frame including colon and CRLF delimiters.
pub fn encode_ascii_frame(unit_id: u8, pdu: &[u8]) -> Vec<u8> {
    let mut binary = Vec::with_capacity(pdu.len() + 2);
    binary.push(unit_id);
    binary.extend_from_slice(pdu);
    binary.push(lrc(&binary));
    let mut frame = Vec::with_capacity(binary.len() * 2 + 3);
    frame.push(b':');
    for byte in binary {
        frame.extend_from_slice(format!("{byte:02X}").as_bytes());
    }
    frame.extend_from_slice(b"\r\n");
    frame
}

/// Validates and decodes one complete Modbus ASCII frame.
pub fn decode_ascii_frame(frame: &[u8]) -> CommResult<(u8, Vec<u8>)> {
    if frame.len() < 9 || frame.first() != Some(&b':') || !frame.ends_with(b"\r\n") {
        return Err(frame_error(
            "MODBUS.ASCII.FORMAT",
            "ASCII frame must contain colon, data, LRC, and CRLF",
        ));
    }
    let hexadecimal = &frame[1..frame.len() - 2];
    if !hexadecimal.len().is_multiple_of(2) {
        return Err(frame_error(
            "MODBUS.ASCII.HEX",
            "ASCII payload has odd length",
        ));
    }
    let mut binary = Vec::with_capacity(hexadecimal.len() / 2);
    for pair in hexadecimal.chunks_exact(2) {
        let text = std::str::from_utf8(pair)
            .map_err(|_| frame_error("MODBUS.ASCII.HEX", "ASCII payload is not UTF-8"))?;
        binary.push(u8::from_str_radix(text, 16).map_err(|_| {
            frame_error(
                "MODBUS.ASCII.HEX",
                "ASCII payload contains non-hex characters",
            )
        })?);
    }
    if binary.len() < 3 {
        return Err(frame_error(
            "MODBUS.ASCII.LENGTH",
            "ASCII payload is too short",
        ));
    }
    let received = binary.pop().expect("validated non-empty payload");
    if lrc(&binary) != received {
        return Err(frame_error(
            "MODBUS.ASCII.LRC",
            "ASCII LRC validation failed",
        ));
    }
    Ok((binary[0], binary[1..].to_vec()))
}

fn crc16(payload: &[u8]) -> u16 {
    let mut crc = 0xFFFF_u16;
    for byte in payload {
        crc ^= u16::from(*byte);
        for _ in 0..8 {
            crc = if crc & 1 != 0 {
                (crc >> 1) ^ 0xA001
            } else {
                crc >> 1
            };
        }
    }
    crc
}

fn lrc(payload: &[u8]) -> u8 {
    (0_u8).wrapping_sub(
        payload
            .iter()
            .fold(0_u8, |sum, byte| sum.wrapping_add(*byte)),
    )
}

fn frame_error(code: &str, message: &str) -> CommError {
    CommError::new(code, CommErrorCategory::Protocol, message, false)
}

#[cfg(test)]
mod tests {
    use super::{decode_ascii_frame, decode_rtu_frame, encode_ascii_frame, encode_rtu_frame};

    #[test]
    fn matches_modbus_golden_frames() {
        let pdu = [3, 0, 0x6B, 0, 3];
        assert_eq!(
            encode_rtu_frame(0x11, &pdu),
            [0x11, 3, 0, 0x6B, 0, 3, 0x76, 0x87]
        );
        assert_eq!(encode_ascii_frame(0x11, &pdu), b":1103006B00037E\r\n");
        assert_eq!(
            decode_rtu_frame(&encode_rtu_frame(0x11, &pdu)).unwrap(),
            (0x11, pdu.to_vec())
        );
        assert_eq!(
            decode_ascii_frame(&encode_ascii_frame(0x11, &pdu)).unwrap(),
            (0x11, pdu.to_vec())
        );
    }
}
