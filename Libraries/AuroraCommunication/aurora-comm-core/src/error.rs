//! Stable communication errors shared by every protocol.

use serde::{Deserialize, Serialize};
use std::fmt::{Display, Formatter};

/// Broad error categories that callers can handle without parsing text.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[non_exhaustive]
pub enum CommErrorCategory {
    /// The supplied endpoint, address, or option is invalid.
    Configuration,
    /// A socket, serial port, or operating-system transport failed.
    Transport,
    /// The operation exceeded its deadline.
    Timeout,
    /// The caller cancelled the operation.
    Cancelled,
    /// The remote protocol rejected or could not decode the operation.
    Protocol,
    /// Authentication or certificate validation failed.
    Authentication,
    /// The device explicitly rejected an otherwise valid request.
    DeviceRejected,
    /// A bounded runtime queue could not accept more work.
    Backpressure,
    /// The connection is not currently usable.
    Unavailable,
    /// An invariant inside the library was violated.
    Internal,
}

/// A stable, structured communication failure.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CommError {
    /// Stable library-level code suitable for UI localization.
    pub code: String,
    /// Broad programmatic category.
    pub category: CommErrorCategory,
    /// Optional protocol-specific numeric error code.
    pub protocol_code: Option<i32>,
    /// English technical message for diagnostics.
    pub message: String,
    /// Whether retrying later can reasonably succeed.
    pub retryable: bool,
}

impl CommError {
    /// Builds an error without a protocol-specific numeric code.
    pub fn new(
        code: impl Into<String>,
        category: CommErrorCategory,
        message: impl Into<String>,
        retryable: bool,
    ) -> Self {
        Self {
            code: code.into(),
            category,
            protocol_code: None,
            message: message.into(),
            retryable,
        }
    }

    /// Attaches a protocol-specific numeric error code.
    #[must_use]
    pub const fn with_protocol_code(mut self, protocol_code: i32) -> Self {
        self.protocol_code = Some(protocol_code);
        self
    }

    /// Returns true when the failure invalidates the active connection.
    pub const fn invalidates_connection(&self) -> bool {
        matches!(
            self.category,
            CommErrorCategory::Transport
                | CommErrorCategory::Timeout
                | CommErrorCategory::Unavailable
        )
    }
}

impl Display for CommError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{}: {}", self.code, self.message)
    }
}

impl std::error::Error for CommError {}

/// The result type returned by all communication operations.
pub type CommResult<T> = Result<T, CommError>;
