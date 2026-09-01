//! Aurora 2.0 control-core building blocks.

pub mod actor;
pub mod api;
pub mod device_vision_api;
pub mod lease;
pub mod persistence;

/// Generated cross-process contracts shared with the HMI host.
#[allow(missing_docs, clippy::all, clippy::pedantic)]
pub mod contracts {
    tonic::include_proto!("aurora.v2");
}
