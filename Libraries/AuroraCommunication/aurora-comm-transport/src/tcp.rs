//! Serialized TCP byte stream used by request/response protocols.

use async_trait::async_trait;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};
use std::io::ErrorKind;
use std::time::Duration;
use std::time::Instant;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::Mutex;
use tokio::time::timeout;

/// Operations required from a connected byte stream.
#[async_trait]
pub trait ByteStream: Send + Sync {
    /// Establishes the transport connection.
    async fn connect(&self) -> CommResult<()>;
    /// Closes the transport connection.
    async fn disconnect(&self) -> CommResult<()>;
    /// Writes the complete payload before the deadline.
    async fn write_all(&self, payload: &[u8], deadline: Duration) -> CommResult<()>;
    /// Reads exactly `length` bytes before the deadline.
    async fn read_exact(&self, length: usize, deadline: Duration) -> CommResult<Vec<u8>>;
    /// Reads through the delimiter with a bounded frame size.
    async fn read_until(
        &self,
        delimiter: u8,
        maximum: usize,
        deadline: Duration,
    ) -> CommResult<Vec<u8>> {
        let started = Instant::now();
        let mut payload = Vec::new();
        while payload.len() < maximum {
            let remaining = deadline.checked_sub(started.elapsed()).ok_or_else(|| {
                CommError::new(
                    "COMM.TIMEOUT",
                    CommErrorCategory::Timeout,
                    "Delimited frame read exceeded its deadline",
                    true,
                )
            })?;
            let next = self.read_exact(1, remaining).await?[0];
            payload.push(next);
            if next == delimiter {
                return Ok(payload);
            }
        }
        Err(CommError::new(
            "COMM.PROTOCOL.FRAME_TOO_LARGE",
            CommErrorCategory::Protocol,
            format!("Delimited frame exceeded {maximum} bytes"),
            false,
        ))
    }
    /// Reports whether a stream is currently installed.
    async fn is_connected(&self) -> bool;
}

/// Strongly typed TCP connection settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TcpTransportOptions {
    /// DNS name or IP address.
    pub host: String,
    /// TCP port.
    pub port: u16,
    /// Maximum connection-establishment duration.
    pub connect_timeout: Duration,
    /// Enables `TCP_NODELAY` for request/response protocols.
    pub no_delay: bool,
}

impl TcpTransportOptions {
    /// Creates options with industrial-client defaults.
    pub fn new(host: impl Into<String>, port: u16) -> Self {
        Self {
            host: host.into(),
            port,
            connect_timeout: Duration::from_secs(5),
            no_delay: true,
        }
    }
}

/// A reconnectable TCP transport with a single owned stream.
pub struct TcpTransport {
    options: TcpTransportOptions,
    stream: Mutex<Option<TcpStream>>,
}

impl TcpTransport {
    /// Creates a disconnected transport.
    pub fn new(options: TcpTransportOptions) -> Self {
        Self {
            options,
            stream: Mutex::new(None),
        }
    }

    fn transport_error(operation: &str, error: &std::io::Error) -> CommError {
        let retryable = matches!(
            error.kind(),
            ErrorKind::ConnectionAborted
                | ErrorKind::ConnectionRefused
                | ErrorKind::ConnectionReset
                | ErrorKind::NotConnected
                | ErrorKind::TimedOut
                | ErrorKind::UnexpectedEof
                | ErrorKind::WouldBlock
        );
        CommError::new(
            "COMM.TRANSPORT.IO",
            CommErrorCategory::Transport,
            format!("TCP {operation} failed: {error}"),
            retryable,
        )
    }

    fn timeout_error(operation: &str, duration: Duration) -> CommError {
        CommError::new(
            "COMM.TIMEOUT",
            CommErrorCategory::Timeout,
            format!("TCP {operation} exceeded {duration:?}"),
            true,
        )
    }
}

#[async_trait]
impl ByteStream for TcpTransport {
    async fn connect(&self) -> CommResult<()> {
        let mut guard = self.stream.lock().await;
        if guard.is_some() {
            return Ok(());
        }

        let endpoint = (self.options.host.as_str(), self.options.port);
        let stream = timeout(self.options.connect_timeout, TcpStream::connect(endpoint))
            .await
            .map_err(|_| Self::timeout_error("connect", self.options.connect_timeout))?
            .map_err(|error| Self::transport_error("connect", &error))?;
        stream
            .set_nodelay(self.options.no_delay)
            .map_err(|error| Self::transport_error("configure", &error))?;
        *guard = Some(stream);
        Ok(())
    }

    async fn disconnect(&self) -> CommResult<()> {
        let mut guard = self.stream.lock().await;
        if let Some(mut stream) = guard.take() {
            stream
                .shutdown()
                .await
                .map_err(|error| Self::transport_error("disconnect", &error))?;
        }
        Ok(())
    }

    async fn write_all(&self, payload: &[u8], deadline: Duration) -> CommResult<()> {
        let mut guard = self.stream.lock().await;
        let stream = guard.as_mut().ok_or_else(|| {
            CommError::new(
                "COMM.UNAVAILABLE.DISCONNECTED",
                CommErrorCategory::Unavailable,
                "TCP stream is disconnected",
                true,
            )
        })?;
        timeout(deadline, stream.write_all(payload))
            .await
            .map_err(|_| Self::timeout_error("write", deadline))?
            .map_err(|error| Self::transport_error("write", &error))
    }

    async fn read_exact(&self, length: usize, deadline: Duration) -> CommResult<Vec<u8>> {
        let mut guard = self.stream.lock().await;
        let stream = guard.as_mut().ok_or_else(|| {
            CommError::new(
                "COMM.UNAVAILABLE.DISCONNECTED",
                CommErrorCategory::Unavailable,
                "TCP stream is disconnected",
                true,
            )
        })?;
        let mut payload = vec![0_u8; length];
        timeout(deadline, stream.read_exact(&mut payload))
            .await
            .map_err(|_| Self::timeout_error("read", deadline))?
            .map_err(|error| Self::transport_error("read", &error))?;
        Ok(payload)
    }

    async fn is_connected(&self) -> bool {
        self.stream.lock().await.is_some()
    }
}
