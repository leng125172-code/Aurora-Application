//! Mitsubishi device-code address parser.

use aurora_comm_core::{CommError, CommErrorCategory};
use std::str::FromStr;

/// Parsed MC device address.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct McAddress {
    /// Three-byte device offset.
    pub offset: u32,
    /// Binary MC device code.
    pub device_code: u8,
    /// Two-character ASCII MC code.
    pub ascii_code: [u8; 2],
    /// Whether numeric addresses use hexadecimal notation.
    pub hexadecimal: bool,
    /// Whether this is normally a bit device.
    pub bit_device: bool,
}

impl FromStr for McAddress {
    type Err = CommError;
    fn from_str(value: &str) -> Result<Self, Self::Err> {
        let upper = value.trim().to_ascii_uppercase();
        let name = [
            "ZR", "SD", "SM", "TN", "CN", "TS", "TC", "CS", "CC", "D", "R", "W", "M", "X", "Y",
            "B", "L", "F", "V", "S",
        ]
        .into_iter()
        .find(|prefix| upper.starts_with(prefix))
        .ok_or_else(|| invalid("unsupported MC device"))?;
        let number = &upper[name.len()..];
        if number.is_empty() {
            return Err(invalid("MC address has no offset"));
        }
        let (device_code, ascii_code, hexadecimal, bit_device) = match name {
            "D" => (0xa8, *b"D*", false, false),
            "R" => (0xaf, *b"R*", false, false),
            "ZR" => (0xb0, *b"ZR", true, false),
            "W" => (0xb4, *b"W*", true, false),
            "SD" => (0xa9, *b"SD", false, false),
            "TN" => (0xc2, *b"TN", false, false),
            "CN" => (0xc5, *b"CN", false, false),
            "M" => (0x90, *b"M*", false, true),
            "X" => (0x9c, *b"X*", true, true),
            "Y" => (0x9d, *b"Y*", true, true),
            "B" => (0xa0, *b"B*", true, true),
            "L" => (0x92, *b"L*", false, true),
            "F" => (0x93, *b"F*", false, true),
            "V" => (0x94, *b"V*", false, true),
            "S" => (0x98, *b"S*", false, true),
            "SM" => (0x91, *b"SM", false, true),
            "TS" => (0xc1, *b"TS", false, true),
            "TC" => (0xc0, *b"TC", false, true),
            "CS" => (0xc4, *b"CS", false, true),
            "CC" => (0xc3, *b"CC", false, true),
            _ => return Err(invalid(format!("unsupported MC device {name}"))),
        };
        let offset = u32::from_str_radix(number, if hexadecimal { 16 } else { 10 })
            .map_err(|_| invalid("invalid MC device offset"))?;
        if offset > 0x00ff_ffff {
            return Err(invalid("MC device offset exceeds 24 bits"));
        }
        Ok(Self {
            offset,
            device_code,
            ascii_code,
            hexadecimal,
            bit_device,
        })
    }
}

fn invalid(message: impl Into<String>) -> CommError {
    CommError::new(
        "MC3E.ADDRESS.INVALID",
        CommErrorCategory::Configuration,
        message,
        false,
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn parses_decimal_and_hex_devices() {
        assert_eq!("D100".parse::<McAddress>().unwrap().offset, 100);
        assert_eq!("X1A".parse::<McAddress>().unwrap().offset, 0x1a);
        assert!("ZRFFFFFF".parse::<McAddress>().is_ok());
    }
}
