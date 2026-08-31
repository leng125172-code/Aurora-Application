//! Pure Modbus frame and value codecs.

use aurora_comm_core::{CommError, CommErrorCategory, CommResult, DeviceDataType, DeviceValue};

pub(crate) fn read_pdu(function: u8, offset: u16, quantity: u16) -> Vec<u8> {
    let mut pdu = Vec::with_capacity(5);
    pdu.push(function);
    pdu.extend_from_slice(&offset.to_be_bytes());
    pdu.extend_from_slice(&quantity.to_be_bytes());
    pdu
}

pub(crate) fn register_width(data_type: DeviceDataType) -> CommResult<u16> {
    match data_type {
        DeviceDataType::UInt16 | DeviceDataType::Int16 | DeviceDataType::Bytes => Ok(1),
        DeviceDataType::UInt32 | DeviceDataType::Int32 | DeviceDataType::Float32 => Ok(2),
        DeviceDataType::UInt64 | DeviceDataType::Int64 | DeviceDataType::Float64 => Ok(4),
        DeviceDataType::Bool | DeviceDataType::String => Err(CommError::new(
            "MODBUS.TYPE.UNSUPPORTED_REGISTER",
            CommErrorCategory::Configuration,
            "The selected value type is not a Modbus register type",
            false,
        )),
        _ => Err(CommError::new(
            "MODBUS.TYPE.UNSUPPORTED_REGISTER",
            CommErrorCategory::Configuration,
            "The selected value type is not supported by this Modbus implementation",
            false,
        )),
    }
}

pub(crate) fn decode_bits(payload: &[u8], count: u16) -> CommResult<DeviceValue> {
    let expected = usize::from(count).div_ceil(8);
    if payload.len() < expected {
        return Err(CommError::new(
            "MODBUS.RESPONSE.LENGTH",
            CommErrorCategory::Protocol,
            format!(
                "Expected at least {expected} coil bytes but received {}",
                payload.len()
            ),
            false,
        ));
    }

    let values = (0..usize::from(count))
        .map(|index| payload[index / 8] & (1 << (index % 8)) != 0)
        .collect::<Vec<_>>();
    if count == 1 {
        Ok(DeviceValue::Bool(values[0]))
    } else {
        Ok(DeviceValue::Bools(values))
    }
}

pub(crate) fn decode_registers(
    payload: &[u8],
    data_type: DeviceDataType,
    count: u16,
) -> CommResult<DeviceValue> {
    let expected = usize::from(register_width(data_type)? * count) * 2;
    if payload.len() != expected {
        return Err(CommError::new(
            "MODBUS.RESPONSE.LENGTH",
            CommErrorCategory::Protocol,
            format!(
                "Expected {expected} register bytes but received {}",
                payload.len()
            ),
            false,
        ));
    }

    macro_rules! decode {
        ($size:literal, $convert:expr, $scalar:ident, $array:ident) => {{
            let values = payload
                .chunks_exact($size)
                .map($convert)
                .collect::<Vec<_>>();
            if count == 1 {
                DeviceValue::$scalar(values[0])
            } else {
                DeviceValue::$array(values)
            }
        }};
    }

    Ok(match data_type {
        DeviceDataType::UInt16 => decode!(
            2,
            |chunk: &[u8]| u16::from_be_bytes([chunk[0], chunk[1]]),
            UInt16,
            UInt16s
        ),
        DeviceDataType::Int16 => decode!(
            2,
            |chunk: &[u8]| i16::from_be_bytes([chunk[0], chunk[1]]),
            Int16,
            Int16s
        ),
        DeviceDataType::UInt32 => decode!(
            4,
            |chunk: &[u8]| u32::from_be_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]),
            UInt32,
            UInt32s
        ),
        DeviceDataType::Int32 => decode!(
            4,
            |chunk: &[u8]| i32::from_be_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]),
            Int32,
            Int32s
        ),
        DeviceDataType::UInt64 => decode!(
            8,
            |chunk: &[u8]| u64::from_be_bytes(chunk.try_into().expect("fixed chunk")),
            UInt64,
            UInt64s
        ),
        DeviceDataType::Int64 => decode!(
            8,
            |chunk: &[u8]| i64::from_be_bytes(chunk.try_into().expect("fixed chunk")),
            Int64,
            Int64s
        ),
        DeviceDataType::Float32 => decode!(
            4,
            |chunk: &[u8]| f32::from_be_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]),
            Float32,
            Float32s
        ),
        DeviceDataType::Float64 => decode!(
            8,
            |chunk: &[u8]| f64::from_be_bytes(chunk.try_into().expect("fixed chunk")),
            Float64,
            Float64s
        ),
        DeviceDataType::Bytes => DeviceValue::Bytes(payload.to_vec()),
        DeviceDataType::Bool | DeviceDataType::String => unreachable!("validated register type"),
        _ => {
            return Err(CommError::new(
                "MODBUS.TYPE.UNSUPPORTED_REGISTER",
                CommErrorCategory::Configuration,
                "The selected value type is not supported by this Modbus implementation",
                false,
            ));
        }
    })
}

pub(crate) fn encode_write(
    address_function: u8,
    offset: u16,
    value: &DeviceValue,
) -> CommResult<Vec<u8>> {
    match value {
        DeviceValue::Bool(value) if address_function == 1 => {
            let encoded = if *value { 0xFF00_u16 } else { 0_u16 };
            let mut pdu = vec![5];
            pdu.extend_from_slice(&offset.to_be_bytes());
            pdu.extend_from_slice(&encoded.to_be_bytes());
            Ok(pdu)
        }
        DeviceValue::Bools(values) if address_function == 1 => {
            let quantity = u16::try_from(values.len()).map_err(|_| write_too_large())?;
            let byte_count = values.len().div_ceil(8);
            let mut data = vec![0_u8; byte_count];
            for (index, value) in values.iter().enumerate() {
                if *value {
                    data[index / 8] |= 1 << (index % 8);
                }
            }
            let mut pdu = vec![15];
            pdu.extend_from_slice(&offset.to_be_bytes());
            pdu.extend_from_slice(&quantity.to_be_bytes());
            pdu.push(u8::try_from(byte_count).map_err(|_| write_too_large())?);
            pdu.extend_from_slice(&data);
            Ok(pdu)
        }
        value if address_function == 3 => encode_register_write(offset, value),
        _ => Err(CommError::new(
            "MODBUS.WRITE.TYPE_OR_AREA",
            CommErrorCategory::Configuration,
            "The value type is not writable at the selected Modbus area",
            false,
        )),
    }
}

fn encode_register_write(offset: u16, value: &DeviceValue) -> CommResult<Vec<u8>> {
    let mut data = Vec::new();
    match value {
        DeviceValue::UInt16(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::Int16(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::UInt32(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::Int32(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::UInt64(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::Int64(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::Float32(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::Float64(value) => data.extend_from_slice(&value.to_be_bytes()),
        DeviceValue::UInt16s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Int16s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::UInt32s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Int32s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::UInt64s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Int64s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Float32s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Float64s(values) => values
            .iter()
            .for_each(|value| data.extend_from_slice(&value.to_be_bytes())),
        DeviceValue::Bytes(values) => data.extend_from_slice(values),
        DeviceValue::Bool(_) | DeviceValue::Bools(_) | DeviceValue::String(_) => {
            return Err(CommError::new(
                "MODBUS.WRITE.TYPE",
                CommErrorCategory::Configuration,
                "The selected value is not a Modbus register value",
                false,
            ));
        }
        _ => {
            return Err(CommError::new(
                "MODBUS.WRITE.TYPE",
                CommErrorCategory::Configuration,
                "The selected value is not supported by this Modbus implementation",
                false,
            ));
        }
    }

    if data.is_empty() || data.len() % 2 != 0 {
        return Err(CommError::new(
            "MODBUS.WRITE.REGISTER_LENGTH",
            CommErrorCategory::Configuration,
            "Register writes must contain a non-empty even number of bytes",
            false,
        ));
    }

    let quantity = u16::try_from(data.len() / 2).map_err(|_| write_too_large())?;
    if quantity == 1 {
        let mut pdu = vec![6];
        pdu.extend_from_slice(&offset.to_be_bytes());
        pdu.extend_from_slice(&data);
        return Ok(pdu);
    }

    let mut pdu = vec![16];
    pdu.extend_from_slice(&offset.to_be_bytes());
    pdu.extend_from_slice(&quantity.to_be_bytes());
    pdu.push(u8::try_from(data.len()).map_err(|_| write_too_large())?);
    pdu.extend_from_slice(&data);
    Ok(pdu)
}

fn write_too_large() -> CommError {
    CommError::new(
        "MODBUS.WRITE.TOO_LARGE",
        CommErrorCategory::Configuration,
        "Modbus write payload exceeds protocol limits",
        false,
    )
}
