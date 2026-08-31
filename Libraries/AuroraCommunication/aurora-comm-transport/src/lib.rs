//! Cross-platform transport primitives backed by Tokio.

mod tcp;

pub use tcp::{ByteStream, TcpTransport, TcpTransportOptions};
