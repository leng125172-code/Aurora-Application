//! Cross-platform Tokio serial byte stream.

use crate::ByteStream;
use async_trait::async_trait;
use aurora_comm_core::{CommError, CommErrorCategory, CommResult};
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::sync::Mutex;
use tokio::time::timeout;
use tokio_serial::{DataBits, FlowControl, Parity, SerialPortBuilderExt, SerialStream, StopBits};

/// Supported serial parity settings.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum SerialParity {
    /// No parity bit.
    #[default]
    None,
    /// Odd parity.
    Odd,
    /// Even parity.
    Even,
}

/// Supported serial stop-bit settings.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum SerialStopBits {
    /// One stop bit.
    #[default]
    One,
    /// Two stop bits.
    Two,
}

/// Serial-port connection settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SerialTransportOptions {
    /// Operating-system port name, such as `COM3` or `/dev/ttyS3`.
    pub port: String,
    /// Baud rate.
    pub baud_rate: u32,
    /// Number of data bits, from five through eight.
    pub data_bits: u8,
    /// Parity mode.
    pub parity: SerialParity,
    /// Stop-bit mode.
    pub stop_bits: SerialStopBits,
    /// Maximum time allowed to open the port.
    pub connect_timeout: Duration,
}

impl SerialTransportOptions {
    /// Creates the common 8-N-1 configuration.
    pub fn new(port: impl Into<String>, baud_rate: u32) -> Self {
        Self {
            port: port.into(),
            baud_rate,
            data_bits: 8,
            parity: SerialParity::None,
            stop_bits: SerialStopBits::One,
            connect_timeout: Duration::from_secs(5),
        }
    }
}

/// A reconnectable, process-local exclusive serial stream.
pub struct SerialTransport {
    options: SerialTransportOptions,
    stream: Mutex<Option<SerialStream>>,
}

impl SerialTransport {
    /// Creates a disconnected serial transport.
    pub fn new(options: SerialTransportOptions) -> Self {
        Self {
            options,
            stream: Mutex::new(None),
        }
    }

    fn error(operation: &str, error: &impl std::fmt::Display) -> CommError {
        CommError::new(
            "COMM.TRANSPORT.SERIAL",
            CommErrorCategory::Transport,
            format!("Serial {operation} failed: {error}"),
            true,
        )
    }

    fn timeout(operation: &str, duration: Duration) -> CommError {
        CommError::new(
            "COMM.TIMEOUT",
            CommErrorCategory::Timeout,
            format!("Serial {operation} exceeded {duration:?}"),
            true,
        )
    }
}

#[async_trait]
impl ByteStream for SerialTransport {
    async fn connect(&self) -> CommResult<()> {
        let mut guard = self.stream.lock().await;
        if guard.is_some() {
            return Ok(());
        }
        let data_bits = match self.options.data_bits {
            5 => DataBits::Five,
            6 => DataBits::Six,
            7 => DataBits::Seven,
            8 => DataBits::Eight,
            value => {
                return Err(CommError::new(
                    "COMM.CONFIG.SERIAL_DATA_BITS",
                    CommErrorCategory::Configuration,
                    format!("Unsupported serial data bits: {value}"),
                    false,
                ));
            }
        };
        let parity = match self.options.parity {
            SerialParity::None => Parity::None,
            SerialParity::Odd => Parity::Odd,
            SerialParity::Even => Parity::Even,
        };
        let stop_bits = match self.options.stop_bits {
            SerialStopBits::One => StopBits::One,
            SerialStopBits::Two => StopBits::Two,
        };
        let builder = tokio_serial::new(&self.options.port, self.options.baud_rate)
            .data_bits(data_bits)
            .parity(parity)
            .stop_bits(stop_bits)
            .flow_control(FlowControl::None);
        let stream = timeout(self.options.connect_timeout, async move {
            builder.open_native_async()
        })
        .await
        .map_err(|_| Self::timeout("connect", self.options.connect_timeout))?
        .map_err(|error| Self::error("connect", &error))?;
        *guard = Some(stream);
        Ok(())
    }

    async fn disconnect(&self) -> CommResult<()> {
        self.stream.lock().await.take();
        Ok(())
    }

    async fn write_all(&self, payload: &[u8], deadline: Duration) -> CommResult<()> {
        let mut guard = self.stream.lock().await;
        let stream = guard.as_mut().ok_or_else(disconnected)?;
        timeout(deadline, async {
            stream.write_all(payload).await?;
            stream.flush().await
        })
        .await
        .map_err(|_| Self::timeout("write", deadline))?
        .map_err(|error| Self::error("write", &error))
    }

    async fn read_exact(&self, length: usize, deadline: Duration) -> CommResult<Vec<u8>> {
        let mut guard = self.stream.lock().await;
        let stream = guard.as_mut().ok_or_else(disconnected)?;
        let mut payload = vec![0; length];
        timeout(deadline, stream.read_exact(&mut payload))
            .await
            .map_err(|_| Self::timeout("read", deadline))?
            .map_err(|error| Self::error("read", &error))?;
        Ok(payload)
    }

    async fn is_connected(&self) -> bool {
        self.stream.lock().await.is_some()
    }
}

fn disconnected() -> CommError {
    CommError::new(
        "COMM.UNAVAILABLE.DISCONNECTED",
        CommErrorCategory::Unavailable,
        "Serial stream is disconnected",
        true,
    )
}
