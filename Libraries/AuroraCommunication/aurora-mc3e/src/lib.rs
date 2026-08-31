//! Mitsubishi MC protocol 3E binary/ASCII implementation.

mod address;
mod client;
mod codec;

pub use address::McAddress;
pub use client::{Mc3eClient, Mc3eFrame, Mc3eOptions, Mc3eTransport};
pub use codec::{
    build_ascii_batch, build_binary_batch, parse_ascii_response, parse_binary_response,
};
