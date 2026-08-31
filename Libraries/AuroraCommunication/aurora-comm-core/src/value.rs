//! Strongly typed device values and updates.

use serde::{Deserialize, Serialize};
use std::time::SystemTime;

/// Supported protocol-neutral value types.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[non_exhaustive]
pub enum DeviceDataType {
    /// Boolean value.
    Bool,
    /// Unsigned 16-bit integer.
    UInt16,
    /// Signed 16-bit integer.
    Int16,
    /// Unsigned 32-bit integer.
    UInt32,
    /// Signed 32-bit integer.
    Int32,
    /// Unsigned 64-bit integer.
    UInt64,
    /// Signed 64-bit integer.
    Int64,
    /// IEEE-754 single-precision value.
    Float32,
    /// IEEE-754 double-precision value.
    Float64,
    /// UTF-8 string.
    String,
    /// Opaque bytes.
    Bytes,
}

/// A value returned by or written to a device.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[non_exhaustive]
pub enum DeviceValue {
    /// One boolean.
    Bool(bool),
    /// Multiple booleans.
    Bools(Vec<bool>),
    /// One unsigned 16-bit integer.
    UInt16(u16),
    /// Multiple unsigned 16-bit integers.
    UInt16s(Vec<u16>),
    /// One signed 16-bit integer.
    Int16(i16),
    /// Multiple signed 16-bit integers.
    Int16s(Vec<i16>),
    /// One unsigned 32-bit integer.
    UInt32(u32),
    /// Multiple unsigned 32-bit integers.
    UInt32s(Vec<u32>),
    /// One signed 32-bit integer.
    Int32(i32),
    /// Multiple signed 32-bit integers.
    Int32s(Vec<i32>),
    /// One unsigned 64-bit integer.
    UInt64(u64),
    /// Multiple unsigned 64-bit integers.
    UInt64s(Vec<u64>),
    /// One signed 64-bit integer.
    Int64(i64),
    /// Multiple signed 64-bit integers.
    Int64s(Vec<i64>),
    /// One single-precision float.
    Float32(f32),
    /// Multiple single-precision floats.
    Float32s(Vec<f32>),
    /// One double-precision float.
    Float64(f64),
    /// Multiple double-precision floats.
    Float64s(Vec<f64>),
    /// UTF-8 text.
    String(String),
    /// Opaque bytes.
    Bytes(Vec<u8>),
}

/// Quality of the most recent device value.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ValueQuality {
    /// Value is current and valid.
    Good,
    /// Value exists but freshness or validity is uncertain.
    Uncertain,
    /// Device responded but the value is invalid.
    Bad,
    /// No value because the device is disconnected.
    Disconnected,
}

/// A timestamped update published by a watch subscription.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DeviceUpdate {
    /// Application-level point identity.
    pub key: String,
    /// Latest value, absent on failure.
    pub value: Option<DeviceValue>,
    /// Validity of the latest sample.
    pub quality: ValueQuality,
    /// Local receipt timestamp.
    pub received_at: SystemTime,
    /// Stable error code when quality is not good.
    pub error_code: Option<String>,
}
