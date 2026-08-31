//! Feature discovery for protocol-neutral clients.

use serde::{Deserialize, Serialize};

/// Features implemented by a concrete device client.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceCapabilities {
    /// Supports individual reads.
    pub read: bool,
    /// Supports individual writes.
    pub write: bool,
    /// Supports protocol-native or fallback batch reads.
    pub batch_read: bool,
    /// Supports protocol-native or fallback batch writes.
    pub batch_write: bool,
    /// Supports polling or native subscriptions.
    pub watch: bool,
    /// Supports browsing a device namespace.
    pub browse: bool,
    /// Supports authenticated and encrypted channels.
    pub secure_channel: bool,
}

impl DeviceCapabilities {
    /// Baseline request/response device features.
    pub const READ_WRITE: Self = Self {
        read: true,
        write: true,
        batch_read: true,
        batch_write: true,
        watch: true,
        browse: false,
        secure_channel: false,
    };
}
