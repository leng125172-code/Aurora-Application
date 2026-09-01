//! ISO-on-TCP, COTP and S7comm frame codec.

use crate::S7Address;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};

/// Builds the COTP connection request for the selected TSAP pair.
pub fn build_cotp_connect(local_tsap: u16, remote_tsap: u16) -> Vec<u8> {
    let [local_high, local_low] = local_tsap.to_be_bytes();
    let [remote_high, remote_low] = remote_tsap.to_be_bytes();
    vec![
        0x03,
        0x00,
        0x00,
        0x16,
        0x11,
        0xe0,
        0x00,
        0x00,
        0x00,
        0x01,
        0x00,
        0xc1,
        0x02,
        local_high,
        local_low,
        0xc2,
        0x02,
        remote_high,
        remote_low,
        0xc0,
        0x01,
        0x0a,
    ]
}

/// Builds the S7 setup-communication request.
pub fn build_setup_communication(pdu_reference: u16, requested_pdu: u16) -> Vec<u8> {
    let mut s7 = vec![0x32, 0x01, 0, 0];
    s7.extend_from_slice(&pdu_reference.to_be_bytes());
    s7.extend_from_slice(&[0, 8, 0, 0, 0xf0, 0, 0, 1, 0, 1]);
    s7.extend_from_slice(&requested_pdu.to_be_bytes());
    wrap_data(&s7)
}

/// Builds a single-item S7 read-var request.
///
/// # Errors
///
/// Returns a protocol error when the count is zero or the address cannot be
/// represented by an S7 ANY pointer.
pub fn build_read_request(
    pdu_reference: u16,
    address: S7Address,
    count: u16,
    bit: bool,
) -> CommResult<Vec<u8>> {
    build_job(pdu_reference, 4, address, count, bit, &[])
}

/// Builds a single-item S7 write-var request.
///
/// # Errors
///
/// Returns a protocol error when the count is zero, the address is outside the
/// S7 wire range, or the payload exceeds the PDU length fields.
pub fn build_write_request(
    pdu_reference: u16,
    address: S7Address,
    count: u16,
    bit: bool,
    payload: &[u8],
) -> CommResult<Vec<u8>> {
    build_job(pdu_reference, 5, address, count, bit, payload)
}

fn build_job(
    reference: u16,
    function: u8,
    address: S7Address,
    count: u16,
    bit: bool,
    payload: &[u8],
) -> CommResult<Vec<u8>> {
    if count == 0 {
        return Err(protocol("S7 count must be non-zero"));
    }
    let data_len = if function == 5 {
        let padded = payload
            .len()
            .checked_add(usize::from(!payload.len().is_multiple_of(2)))
            .and_then(|length| length.checked_add(4))
            .ok_or_else(|| protocol("S7 payload length overflow"))?;
        u16::try_from(padded).map_err(|_| protocol("S7 payload is too large"))?
    } else {
        0
    };
    let mut s7 = vec![0x32, 0x01, 0, 0];
    s7.extend_from_slice(&reference.to_be_bytes());
    s7.extend_from_slice(&[0, 14]);
    s7.extend_from_slice(&data_len.to_be_bytes());
    s7.extend_from_slice(&[function, 1, 0x12, 0x0a, 0x10, if bit { 1 } else { 2 }]);
    s7.extend_from_slice(&count.to_be_bytes());
    s7.extend_from_slice(&address.db_number.to_be_bytes());
    s7.push(address.area as u8);
    let wire = address.wire_bit_offset()?;
    s7.extend_from_slice(&wire.to_be_bytes()[1..]);
    if function == 5 {
        s7.push(0);
        s7.push(if bit { 3 } else { 4 });
        let length = if bit {
            count
        } else {
            payload
                .len()
                .checked_mul(8)
                .and_then(|length| u16::try_from(length).ok())
                .ok_or_else(|| protocol("S7 payload is too large"))?
        };
        s7.extend_from_slice(&length.to_be_bytes());
        s7.extend_from_slice(payload);
        if !payload.len().is_multiple_of(2) {
            s7.push(0);
        }
    }
    let frame_length = 7_usize
        .checked_add(s7.len())
        .and_then(|length| u16::try_from(length).ok())
        .ok_or_else(|| protocol("S7 TPKT frame is too large"))?;
    Ok(wrap_data_with_length(&s7, frame_length))
}

/// Extracts the payload of a one-item S7 read response.
///
/// # Errors
///
/// Returns a protocol or device error when the TPKT/S7 frame is malformed or
/// the PLC rejects the item.
pub fn parse_read_response(frame: &[u8]) -> CommResult<Vec<u8>> {
    validate_tpkt(frame)?;
    if frame.len() < 25 || frame[7] != 0x32 {
        return Err(protocol("truncated S7 read response"));
    }
    let parameter_length = usize::from(u16::from_be_bytes([frame[13], frame[14]]));
    let data = 19 + parameter_length;
    if frame.get(data).copied() != Some(0xff) {
        return Err(device(frame.get(data).copied().unwrap_or_default()));
    }
    let _transport = frame
        .get(data + 1)
        .copied()
        .ok_or_else(|| protocol("missing S7 transport size"))?;
    let length = u16::from_be_bytes([frame[data + 2], frame[data + 3]]);
    let bytes = usize::from(length.div_ceil(8));
    frame
        .get(data + 4..data + 4 + bytes)
        .map(<[u8]>::to_vec)
        .ok_or_else(|| protocol("truncated S7 data payload"))
}

/// Validates a one-item S7 write response.
///
/// # Errors
///
/// Returns a protocol or device error when the TPKT frame is malformed or the
/// PLC rejects the write.
pub fn parse_write_response(frame: &[u8]) -> CommResult<()> {
    validate_tpkt(frame)?;
    let code = frame.last().copied().unwrap_or_default();
    if code == 0xff {
        Ok(())
    } else {
        Err(device(code))
    }
}

fn wrap_data(s7: &[u8]) -> Vec<u8> {
    let length = u16::try_from(7 + s7.len()).expect("fixed setup frame fits in TPKT length");
    wrap_data_with_length(s7, length)
}

fn wrap_data_with_length(s7: &[u8], length: u16) -> Vec<u8> {
    let mut frame = vec![3, 0];
    frame.extend_from_slice(&length.to_be_bytes());
    frame.extend_from_slice(&[2, 0xf0, 0x80]);
    frame.extend_from_slice(s7);
    frame
}

fn validate_tpkt(frame: &[u8]) -> CommResult<()> {
    if frame.len() < 7
        || frame[0..2] != [3, 0]
        || usize::from(u16::from_be_bytes([frame[2], frame[3]])) != frame.len()
    {
        Err(protocol("invalid ISO-on-TCP frame"))
    } else {
        Ok(())
    }
}

fn protocol(message: impl Into<String>) -> CommError {
    CommError::new(
        "S7.PROTOCOL.INVALID",
        CommErrorCategory::Protocol,
        message,
        false,
    )
}
fn device(code: u8) -> CommError {
    CommError::new(
        "S7.DEVICE.REJECTED",
        CommErrorCategory::DeviceRejected,
        format!("S7 item result 0x{code:02X}"),
        false,
    )
    .with_protocol_code(i32::from(code))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn golden_handshake_and_read_frames() {
        assert_eq!(
            build_cotp_connect(0x0100, 0x0102),
            [
                3, 0, 0, 22, 17, 224, 0, 0, 0, 1, 0, 193, 2, 1, 0, 194, 2, 1, 2, 192, 1, 10
            ]
        );
        let frame = build_read_request(1, "DB1.DBW0".parse().unwrap(), 2, false).unwrap();
        assert_eq!(&frame[..7], &[3, 0, 0, 31, 2, 240, 128]);
        assert_eq!(
            &frame[17..],
            &[4, 1, 18, 10, 16, 2, 0, 2, 0, 1, 132, 0, 0, 0]
        );
    }

    #[test]
    fn rejects_payloads_that_exceed_s7_length_fields() {
        let address = "DB1.DBW0".parse().unwrap();
        assert!(build_write_request(1, address, 1, false, &vec![0; 8_192]).is_err());
        assert!(build_write_request(1, address, 1, true, &vec![0; 65_501]).is_err());
    }
}
