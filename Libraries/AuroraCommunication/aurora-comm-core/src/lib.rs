//! Protocol-neutral contracts for Aurora industrial communication.

mod capabilities;
mod device;
mod error;
mod layout;
mod value;

pub use capabilities::DeviceCapabilities;
pub use device::{
    BatchReadResult, BatchWriteResult, ConnectionState, DeviceClient, DeviceEndpoint, DeviceId,
    DeviceStatus, OperationPriority, ReadRequest, WatchSpec, WriteRequest,
};
pub use error::{CommError, CommErrorCategory, CommResult};
pub use layout::DataLayout;
pub use value::{DeviceDataType, DeviceUpdate, DeviceValue, ValueQuality};
