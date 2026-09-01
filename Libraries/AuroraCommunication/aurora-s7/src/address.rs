//! S7 symbolic address parsing.

use aurora_comm_core::{CommError, CommErrorCategory, CommResult};
use std::str::FromStr;

/// S7 memory-area code.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum S7Area {
    /// Process inputs (I/E).
    Input = 0x81,
    /// Process outputs (Q/A).
    Output = 0x82,
    /// Merker/flag memory (M).
    Marker = 0x83,
    /// Data block memory.
    DataBlock = 0x84,
    /// Timer values.
    Timer = 0x1d,
    /// Counter values.
    Counter = 0x1c,
}

/// Parsed S7 address with a bit-accurate wire offset.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct S7Address {
    /// Memory area.
    pub area: S7Area,
    /// DB number, zero outside DB memory.
    pub db_number: u16,
    /// Byte offset.
    pub byte_offset: u32,
    /// Bit offset (0..=7), present for bit syntax.
    pub bit_offset: Option<u8>,
}

impl S7Address {
    /// Absolute bit offset encoded in an S7 ANY pointer.
    ///
    /// # Errors
    ///
    /// Returns a configuration error when the address cannot fit in the
    /// 24-bit S7 ANY wire offset.
    pub fn wire_bit_offset(self) -> CommResult<u32> {
        self.byte_offset
            .checked_mul(8)
            .and_then(|value| value.checked_add(u32::from(self.bit_offset.unwrap_or(0))))
            .filter(|value| *value <= 0x00ff_ffff)
            .ok_or_else(|| invalid("S7 address exceeds the 24-bit wire offset"))
    }
}

impl FromStr for S7Address {
    type Err = CommError;

    fn from_str(value: &str) -> Result<Self, Self::Err> {
        let text = value.trim().to_ascii_uppercase();
        if let Some(rest) = text.strip_prefix("DB") {
            let (db, member) = rest
                .split_once('.')
                .ok_or_else(|| invalid("expected DBn.DBX/DBB/DBW/DBD address"))?;
            let db_number = number(db)?;
            for prefix in ["DBX", "DBB", "DBW", "DBD"] {
                if let Some(offset) = member.strip_prefix(prefix) {
                    let (byte_offset, bit_offset) = parse_offset(offset, prefix == "DBX")?;
                    return Ok(Self {
                        area: S7Area::DataBlock,
                        db_number,
                        byte_offset,
                        bit_offset,
                    });
                }
            }
            return Err(invalid("unsupported DB address type"));
        }

        let (prefix, rest) = text.split_at(1);
        let area = match prefix {
            "I" | "E" => S7Area::Input,
            "Q" | "A" => S7Area::Output,
            "M" => S7Area::Marker,
            "T" => S7Area::Timer,
            "C" | "Z" => S7Area::Counter,
            _ => return Err(invalid("unsupported S7 memory area")),
        };
        let rest = rest.strip_prefix(['B', 'W', 'D']).unwrap_or(rest);
        let is_bit = rest.contains('.');
        let (byte_offset, bit_offset) = parse_offset(rest, is_bit)?;
        Ok(Self {
            area,
            db_number: 0,
            byte_offset,
            bit_offset,
        })
    }
}

fn parse_offset(value: &str, bit: bool) -> CommResult<(u32, Option<u8>)> {
    if bit {
        let (byte, bit) = value
            .split_once('.')
            .ok_or_else(|| invalid("bit address requires byte.bit"))?;
        let bit = bit
            .parse::<u8>()
            .map_err(|_| invalid("invalid S7 bit offset"))?;
        if bit > 7 {
            return Err(invalid("S7 bit offset must be 0..=7"));
        }
        Ok((
            byte.parse()
                .map_err(|_| invalid("invalid S7 byte offset"))?,
            Some(bit),
        ))
    } else {
        Ok((
            value
                .parse()
                .map_err(|_| invalid("invalid S7 byte offset"))?,
            None,
        ))
    }
}

fn number(value: &str) -> CommResult<u16> {
    value
        .parse()
        .map_err(|_| invalid("invalid S7 block number"))
}

fn invalid(message: impl Into<String>) -> CommError {
    CommError::new(
        "S7.ADDRESS.INVALID",
        CommErrorCategory::Configuration,
        message,
        false,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_common_hsl_address_forms() {
        assert_eq!(
            "DB12.DBX4.3".parse::<S7Address>().unwrap(),
            S7Address {
                area: S7Area::DataBlock,
                db_number: 12,
                byte_offset: 4,
                bit_offset: Some(3)
            }
        );
        assert_eq!(
            "MW100"
                .parse::<S7Address>()
                .unwrap()
                .wire_bit_offset()
                .unwrap(),
            800
        );
        assert_eq!("E2.7".parse::<S7Address>().unwrap().area, S7Area::Input);
        assert_eq!("A4".parse::<S7Address>().unwrap().area, S7Area::Output);
    }
}
