//! Protocol-neutral device lifecycle and operation contracts.

use crate::{CommResult, DeviceDataType, DeviceValue};
use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use std::time::{Duration, SystemTime};
use tokio::sync::watch;
use uuid::Uuid;

/// Stable identity of a configured device.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct DeviceId(Uuid);

impl DeviceId {
    /// Creates a random device identifier.
    pub fn new() -> Self {
        Self(Uuid::new_v4())
    }

    /// Wraps an existing UUID.
    pub const fn from_uuid(value: Uuid) -> Self {
        Self(value)
    }

    /// Returns the underlying UUID.
    pub const fn as_uuid(self) -> Uuid {
        self.0
    }
}

impl Default for DeviceId {
    fn default() -> Self {
        Self::new()
    }
}

/// A strongly typed transport endpoint.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[non_exhaustive]
pub enum DeviceEndpoint {
    /// TCP network endpoint.
    Tcp {
        /// DNS name or IP address.
        host: String,
        /// TCP port.
        port: u16,
    },
    /// UDP network endpoint.
    Udp {
        /// DNS name or IP address.
        host: String,
        /// UDP port.
        port: u16,
    },
    /// Serial-port endpoint.
    Serial {
        /// Operating-system port name.
        port: String,
        /// Serial baud rate.
        baud_rate: u32,
        /// Number of data bits.
        data_bits: u8,
        /// Number of stop bits.
        stop_bits: u8,
        /// Parity mode name.
        parity: String,
    },
    /// OPC UA server endpoint.
    OpcUa {
        /// OPC UA endpoint URL.
        url: String,
    },
}

/// Connection lifecycle visible to callers.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ConnectionState {
    /// No active connection.
    Disconnected,
    /// Connection attempt is in progress.
    Connecting,
    /// Connection is ready for operations.
    Connected,
    /// Waiting before a reconnect attempt.
    Backoff,
    /// Last operation left the connection unusable.
    Faulted,
    /// Graceful shutdown is in progress.
    ShuttingDown,
}

/// Latest connection status and timing information.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceStatus {
    /// Current connection lifecycle state.
    pub state: ConnectionState,
    /// Time at which this status became current.
    pub changed_at: SystemTime,
    /// Stable error code for the most recent connection failure.
    pub last_error_code: Option<String>,
    /// Consecutive reconnect attempt number.
    pub reconnect_attempt: u32,
}

impl DeviceStatus {
    /// Initial disconnected status.
    pub fn disconnected() -> Self {
        Self {
            state: ConnectionState::Disconnected,
            changed_at: SystemTime::now(),
            last_error_code: None,
            reconnect_attempt: 0,
        }
    }
}

/// Scheduling priority used by the managed runtime.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum OperationPriority {
    /// Time-sensitive operator and safety command.
    Control,
    /// Ordinary acquisition or configuration operation.
    Normal,
}

/// A protocol-neutral read request.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReadRequest {
    /// Application-level point identity.
    pub key: String,
    /// Protocol-specific address expression.
    pub address: String,
    /// Expected logical value type.
    pub data_type: DeviceDataType,
    /// Number of logical values.
    pub count: u16,
    /// Per-operation timeout.
    pub timeout: Duration,
}

/// A protocol-neutral write request.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WriteRequest {
    /// Application-level point identity.
    pub key: String,
    /// Protocol-specific address expression.
    pub address: String,
    /// Value to encode and write.
    pub value: DeviceValue,
    /// Per-operation timeout.
    pub timeout: Duration,
}

/// Definition of a managed polling or native subscription.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct WatchSpec {
    /// Read executed on every polling interval.
    pub request: ReadRequest,
    /// Minimum interval between reads.
    pub interval: Duration,
}

/// Minimal protocol client implemented by each device protocol crate.
#[async_trait]
pub trait DeviceClient: Send + Sync {
    /// Establishes the underlying device session.
    async fn connect(&self) -> CommResult<()>;
    /// Closes the underlying device session.
    async fn disconnect(&self) -> CommResult<()>;
    /// Reads a typed value.
    async fn read(&self, request: &ReadRequest) -> CommResult<DeviceValue>;
    /// Writes a typed value.
    async fn write(&self, request: &WriteRequest) -> CommResult<()>;
    /// Subscribes to connection lifecycle changes.
    fn status(&self) -> watch::Receiver<DeviceStatus>;
}
