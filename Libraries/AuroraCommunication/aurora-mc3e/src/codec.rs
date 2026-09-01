//! MC 3E frame construction and response validation.

use crate::McAddress;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};
use std::fmt::Write as _;

/// Builds a binary 3E batch read/write request.
///
/// # Errors
///
/// Returns a protocol error when the point count is zero or the payload is too
/// large for the 3E length field.
pub fn build_binary_batch(
    address: McAddress,
    points: u16,
    write: Option<&[u8]>,
    bit: bool,
    monitoring_timer: u16,
) -> CommResult<Vec<u8>> {
    if points == 0 {
        return Err(protocol("point count must be non-zero"));
    }
    let data_len = 12_usize
        .checked_add(write.map_or(0, <[u8]>::len))
        .and_then(|length| u16::try_from(length).ok())
        .ok_or_else(|| protocol("binary 3E payload exceeds 65535 bytes"))?;
    let mut frame = vec![0x50, 0, 0, 0xff, 0xff, 3, 0];
    frame.extend_from_slice(&data_len.to_le_bytes());
    frame.extend_from_slice(&monitoring_timer.to_le_bytes());
    frame.extend_from_slice(&(if write.is_some() { 0x1401_u16 } else { 0x0401 }).to_le_bytes());
    frame.extend_from_slice(&u16::from(bit).to_le_bytes());
    frame.extend_from_slice(&address.offset.to_le_bytes()[..3]);
    frame.push(address.device_code);
    frame.extend_from_slice(&points.to_le_bytes());
    if let Some(payload) = write {
        frame.extend_from_slice(payload);
    }
    Ok(frame)
}

/// Builds an ASCII 3E batch read/write request.
///
/// # Errors
///
/// Returns a protocol error when the point count is zero or the encoded body
/// is too large for the four-digit 3E length field.
pub fn build_ascii_batch(
    address: McAddress,
    points: u16,
    write: Option<&[u8]>,
    bit: bool,
    monitoring_timer: u16,
) -> CommResult<Vec<u8>> {
    if points == 0 {
        return Err(protocol("point count must be non-zero"));
    }
    let mut payload = String::with_capacity(write.map_or(0, |value| value.len() * 2));
    if let Some(value) = write {
        for byte in value {
            write!(&mut payload, "{byte:02X}")
                .map_err(|_| protocol("failed to encode ASCII 3E payload"))?;
        }
    }
    let ascii_code = std::str::from_utf8(&address.ascii_code)
        .map_err(|_| protocol("MC device code is not ASCII"))?;
    let body = format!(
        "{monitoring_timer:04X}{}{:04X}{}{:06X}{:04X}{payload}",
        if write.is_some() { "1401" } else { "0401" },
        i32::from(bit),
        ascii_code,
        address.offset,
        points
    );
    let body_length = u16::try_from(body.len())
        .map_err(|_| protocol("ASCII 3E body exceeds 65535 characters"))?;
    Ok(format!("500000FF03FF00{body_length:04X}{body}").into_bytes())
}

/// Validates a binary 3E response and returns its data field.
///
/// # Errors
///
/// Returns a protocol or device error when the header, length, or MC end code
/// is invalid.
pub fn parse_binary_response(frame: &[u8]) -> CommResult<Vec<u8>> {
    if frame.len() < 11 || frame[..2] != [0xd0, 0] {
        return Err(protocol("invalid binary 3E response"));
    }
    let length = usize::from(u16::from_le_bytes([frame[7], frame[8]]));
    if frame.len() != 9 + length {
        return Err(protocol("binary 3E response length mismatch"));
    }
    let end = u16::from_le_bytes([frame[9], frame[10]]);
    if end != 0 {
        return Err(device(end));
    }
    Ok(frame[11..].to_vec())
}

/// Validates an ASCII 3E response and returns decoded response bytes.
///
/// # Errors
///
/// Returns a protocol or device error when ASCII, framing, hexadecimal data,
/// length, or the MC end code is invalid.
pub fn parse_ascii_response(frame: &[u8]) -> CommResult<Vec<u8>> {
    if !frame.is_ascii() {
        return Err(protocol("3E ASCII response is not ASCII"));
    }
    let text = std::str::from_utf8(frame).map_err(|_| protocol("invalid ASCII response"))?;
    if text.len() < 22 || !text.starts_with("D000") {
        return Err(protocol("invalid ASCII 3E response"));
    }
    let length = usize::from_str_radix(&text[14..18], 16)
        .map_err(|_| protocol("invalid ASCII 3E length"))?;
    if text.len() != 18 + length {
        return Err(protocol("ASCII 3E response length mismatch"));
    }
    let end = u16::from_str_radix(&text[18..22], 16)
        .map_err(|_| protocol("invalid ASCII 3E end code"))?;
    if end != 0 {
        return Err(device(end));
    }
    let payload = &frame[22..];
    if !payload.len().is_multiple_of(2) {
        return Err(protocol("ASCII 3E payload has odd length"));
    }
    payload
        .as_chunks::<2>()
        .0
        .iter()
        .map(|pair| {
            let text =
                std::str::from_utf8(pair).map_err(|_| protocol("invalid ASCII 3E payload"))?;
            u8::from_str_radix(text, 16).map_err(|_| protocol("invalid ASCII 3E payload"))
        })
        .collect()
}

fn protocol(message: impl Into<String>) -> CommError {
    CommError::new(
        "MC3E.PROTOCOL.INVALID",
        CommErrorCategory::Protocol,
        message,
        false,
    )
}
fn device(code: u16) -> CommError {
    CommError::new(
        "MC3E.DEVICE.REJECTED",
        CommErrorCategory::DeviceRejected,
        format!("MC end code 0x{code:04X}"),
        false,
    )
    .with_protocol_code(i32::from(code))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn golden_binary_and_ascii_read() {
        let a = "D100".parse().unwrap();
        assert_eq!(
            build_binary_batch(a, 2, None, false, 0x10).unwrap(),
            [
                0x50, 0, 0, 0xff, 0xff, 3, 0, 12, 0, 0x10, 0, 1, 4, 0, 0, 100, 0, 0, 0xa8, 2, 0
            ]
        );
        assert_eq!(
            String::from_utf8(build_ascii_batch(a, 2, None, false, 0x10).unwrap()).unwrap(),
            "500000FF03FF000018001004010000D*0000640002"
        );
    }

    #[test]
    fn rejects_frames_that_exceed_wire_length_fields() {
        let address = "D100".parse().unwrap();
        assert!(build_binary_batch(address, 1, Some(&vec![0; 65_524]), false, 0x10).is_err());
        assert!(build_ascii_batch(address, 1, Some(&vec![0; 32_756]), false, 0x10).is_err());
    }

    #[test]
    fn rejects_odd_length_ascii_response_payload() {
        assert!(parse_ascii_response(b"D00000FF03FF00000500000").is_err());
    }
}
