//! Protocol-neutral contracts for Aurora industrial communication.

mod device;
mod error;
mod value;

pub use device::{
    ConnectionState, DeviceClient, DeviceEndpoint, DeviceId, DeviceStatus, OperationPriority,
    ReadRequest, WatchSpec, WriteRequest,
};
pub use error::{CommError, CommErrorCategory, CommResult};
pub use value::{DeviceDataType, DeviceUpdate, DeviceValue, ValueQuality};
