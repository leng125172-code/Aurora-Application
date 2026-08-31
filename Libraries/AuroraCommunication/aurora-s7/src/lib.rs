//! Siemens ISO-on-TCP/S7comm client.

mod address;
mod client;
mod codec;

pub use address::{S7Address, S7Area};
pub use client::{S7Client, S7Model, S7Options};
pub use codec::{
    build_cotp_connect, build_read_request, build_setup_communication, build_write_request,
    parse_read_response, parse_write_response,
};
