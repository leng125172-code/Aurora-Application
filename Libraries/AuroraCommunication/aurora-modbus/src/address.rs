//! HSL-compatible Modbus address parsing.

use aurora_comm_core::{CommError, CommErrorCategory};
use std::str::FromStr;

/// Parsed Modbus address and optional per-operation station override.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ModbusAddress {
    /// Modbus read function code (1-4).
    pub function: u8,
    /// Zero-based protocol address.
    pub offset: u16,
    /// Optional station/unit override from `s=<id>`.
    pub station: Option<u8>,
}

impl FromStr for ModbusAddress {
    type Err = CommError;

    fn from_str(value: &str) -> Result<Self, Self::Err> {
        let mut function = 3_u8;
        let mut station = None;
        let mut offset = None;

        for segment in value
            .split(';')
            .map(str::trim)
            .filter(|item| !item.is_empty())
        {
            if let Some(raw) = segment.strip_prefix("x=") {
                function = raw.parse::<u8>().map_err(|_| invalid_address(value))?;
            } else if let Some(raw) = segment.strip_prefix("s=") {
                station = Some(raw.parse::<u8>().map_err(|_| invalid_address(value))?);
            } else {
                offset = Some(segment.parse::<u16>().map_err(|_| invalid_address(value))?);
            }
        }

        if !(1..=4).contains(&function) {
            return Err(invalid_address(value));
        }

        Ok(Self {
            function,
            offset: offset.ok_or_else(|| invalid_address(value))?,
            station,
        })
    }
}

impl ModbusAddress {
    pub(crate) fn apply_base(&mut self, address_start_with_zero: bool) -> Result<(), CommError> {
        if !address_start_with_zero {
            self.offset = self
                .offset
                .checked_sub(1)
                .ok_or_else(|| invalid_address("one-based address zero"))?;
        }
        Ok(())
    }
}

fn invalid_address(value: &str) -> CommError {
    CommError::new(
        "MODBUS.ADDRESS.INVALID",
        CommErrorCategory::Configuration,
        format!("Invalid Modbus address '{value}'"),
        false,
    )
}

#[cfg(test)]
mod tests {
    use super::ModbusAddress;
    use std::str::FromStr;

    #[test]
    fn parses_hsl_compatible_address_parameters() {
        assert_eq!(
            ModbusAddress::from_str("s=2;x=4;500").expect("address should parse"),
            ModbusAddress {
                function: 4,
                offset: 500,
                station: Some(2),
            }
        );
        assert_eq!(
            ModbusAddress::from_str("300").expect("address should parse"),
            ModbusAddress {
                function: 3,
                offset: 300,
                station: None,
            }
        );
    }
}
