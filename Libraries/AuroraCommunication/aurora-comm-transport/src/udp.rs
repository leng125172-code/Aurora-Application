//! Connected UDP request/response transport.

use async_trait::async_trait;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};
use std::time::Duration;
use tokio::net::UdpSocket;
use tokio::sync::Mutex;
use tokio::time::timeout;

/// Operations required by datagram protocols.
#[async_trait]
pub trait DatagramTransport: Send + Sync {
    /// Creates and connects the datagram socket.
    async fn connect(&self) -> CommResult<()>;
    /// Drops the datagram socket.
    async fn disconnect(&self) -> CommResult<()>;
    /// Sends one datagram and receives one response datagram.
    async fn request_response(
        &self,
        request: &[u8],
        maximum_response: usize,
        deadline: Duration,
    ) -> CommResult<Vec<u8>>;
    /// Reports whether the socket exists.
    async fn is_connected(&self) -> bool;
}

/// UDP endpoint and buffer settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UdpTransportOptions {
    /// Remote DNS name or IP address.
    pub host: String,
    /// Remote UDP port.
    pub port: u16,
    /// Optional local bind address; defaults to an ephemeral wildcard socket.
    pub local_bind: Option<String>,
    /// Maximum socket creation and DNS duration.
    pub connect_timeout: Duration,
}

impl UdpTransportOptions {
    /// Creates UDP options with an ephemeral local port.
    pub fn new(host: impl Into<String>, port: u16) -> Self {
        Self {
            host: host.into(),
            port,
            local_bind: None,
            connect_timeout: Duration::from_secs(5),
        }
    }
}

/// A reconnectable connected UDP transport.
pub struct UdpTransport {
    options: UdpTransportOptions,
    socket: Mutex<Option<UdpSocket>>,
}

impl UdpTransport {
    /// Creates a disconnected UDP transport.
    pub fn new(options: UdpTransportOptions) -> Self {
        Self {
            options,
            socket: Mutex::new(None),
        }
    }
}

#[async_trait]
impl DatagramTransport for UdpTransport {
    async fn connect(&self) -> CommResult<()> {
        let mut guard = self.socket.lock().await;
        if guard.is_some() {
            return Ok(());
        }
        let bind = self.options.local_bind.as_deref().unwrap_or("0.0.0.0:0");
        let remote = format!("{}:{}", self.options.host, self.options.port);
        let socket = timeout(self.options.connect_timeout, async {
            let socket = UdpSocket::bind(bind).await?;
            socket.connect(remote).await?;
            Ok::<_, std::io::Error>(socket)
        })
        .await
        .map_err(|_| timeout_error("connect", self.options.connect_timeout))?
        .map_err(|error| transport_error("connect", &error))?;
        *guard = Some(socket);
        Ok(())
    }

    async fn disconnect(&self) -> CommResult<()> {
        self.socket.lock().await.take();
        Ok(())
    }

    async fn request_response(
        &self,
        request: &[u8],
        maximum_response: usize,
        deadline: Duration,
    ) -> CommResult<Vec<u8>> {
        let guard = self.socket.lock().await;
        let socket = guard.as_ref().ok_or_else(|| {
            CommError::new(
                "COMM.UNAVAILABLE.DISCONNECTED",
                CommErrorCategory::Unavailable,
                "UDP socket is disconnected",
                true,
            )
        })?;
        let mut response = vec![0; maximum_response];
        let length = timeout(deadline, async {
            socket.send(request).await?;
            socket.recv(&mut response).await
        })
        .await
        .map_err(|_| timeout_error("transaction", deadline))?
        .map_err(|error| transport_error("transaction", &error))?;
        response.truncate(length);
        Ok(response)
    }

    async fn is_connected(&self) -> bool {
        self.socket.lock().await.is_some()
    }
}

fn transport_error(operation: &str, error: &std::io::Error) -> CommError {
    CommError::new(
        "COMM.TRANSPORT.UDP",
        CommErrorCategory::Transport,
        format!("UDP {operation} failed: {error}"),
        true,
    )
}

fn timeout_error(operation: &str, duration: Duration) -> CommError {
    CommError::new(
        "COMM.TIMEOUT",
        CommErrorCategory::Timeout,
        format!("UDP {operation} exceeded {duration:?}"),
        true,
    )
}
