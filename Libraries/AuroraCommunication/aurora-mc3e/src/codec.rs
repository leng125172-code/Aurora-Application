//! MC 3E frame construction and response validation.

use crate::McAddress;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};

/// Builds a binary 3E batch read/write request.
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
    let data_len = 12 + write.map_or(0, <[u8]>::len);
    let mut frame = vec![0x50, 0, 0, 0xff, 0xff, 3, 0];
    frame.extend_from_slice(&(data_len as u16).to_le_bytes());
    frame.extend_from_slice(&monitoring_timer.to_le_bytes());
    frame.extend_from_slice(&(if write.is_some() { 0x1401_u16 } else { 0x0401 }).to_le_bytes());
    frame.extend_from_slice(&(if bit { 1_u16 } else { 0 }).to_le_bytes());
    frame.extend_from_slice(&address.offset.to_le_bytes()[..3]);
    frame.push(address.device_code);
    frame.extend_from_slice(&points.to_le_bytes());
    if let Some(payload) = write {
        frame.extend_from_slice(payload);
    }
    Ok(frame)
}

/// Builds an ASCII 3E batch read/write request.
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
    let payload = write.map_or_else(String::new, |v| {
        v.iter().map(|b| format!("{b:02X}")).collect()
    });
    let body = format!(
        "{monitoring_timer:04X}{}{:04X}{}{:06X}{:04X}{payload}",
        if write.is_some() { "1401" } else { "0401" },
        if bit { 1 } else { 0 },
        std::str::from_utf8(&address.ascii_code).unwrap(),
        address.offset,
        points
    );
    Ok(format!("500000FF03FF00{:04X}{body}", body.len()).into_bytes())
}

/// Validates a binary 3E response and returns its data field.
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
pub fn parse_ascii_response(frame: &[u8]) -> CommResult<Vec<u8>> {
    let text =
        std::str::from_utf8(frame).map_err(|_| protocol("3E ASCII response is not ASCII"))?;
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
    text[22..]
        .as_bytes()
        .chunks_exact(2)
        .map(|pair| {
            u8::from_str_radix(std::str::from_utf8(pair).unwrap(), 16)
                .map_err(|_| protocol("invalid ASCII 3E payload"))
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
}
