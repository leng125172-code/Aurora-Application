//! Safe OPC UA API with a deterministic simulator and optional native backend.

use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DeviceCapabilities, DeviceClient,
    DeviceStatus, DeviceValue, ReadRequest, WriteRequest,
};
use std::{
    collections::HashMap,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::SystemTime,
};
use tokio::sync::{RwLock, broadcast, watch};

/// OPC UA message security mode.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MessageSecurityMode {
    /// No signing or encryption.
    None,
    /// Signed messages.
    Sign,
    /// Signed and encrypted messages.
    SignAndEncrypt,
}

/// OPC UA security policy selection.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SecurityPolicy {
    /// No security policy.
    None,
    /// `Basic256Sha256`.
    Basic256Sha256,
    /// AES-128/SHA-256/RSA-OAEP.
    Aes128Sha256RsaOaep,
    /// AES-256/SHA-256/RSA-PSS.
    Aes256Sha256RsaPss,
}

/// Endpoint identity and certificate configuration.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OpcUaOptions {
    /// `opc.tcp://` endpoint URL.
    pub endpoint: String,
    /// Message security mode.
    pub security_mode: MessageSecurityMode,
    /// Security policy.
    pub security_policy: SecurityPolicy,
    /// DER/PEM application certificate.
    pub certificate: Option<Vec<u8>>,
    /// DER/PEM private key.
    pub private_key: Option<Vec<u8>>,
    /// Trusted issuer/server certificates.
    pub trust_list: Vec<Vec<u8>>,
    /// Optional username.
    pub username: Option<String>,
    /// Optional password.
    pub password: Option<String>,
}

impl OpcUaOptions {
    /// Creates an anonymous, unsecured endpoint configuration.
    pub fn anonymous(endpoint: impl Into<String>) -> Self {
        Self {
            endpoint: endpoint.into(),
            security_mode: MessageSecurityMode::None,
            security_policy: SecurityPolicy::None,
            certificate: None,
            private_key: None,
            trust_list: Vec::new(),
            username: None,
            password: None,
        }
    }

    /// Rejects unsafe or incomplete combinations before native code runs.
    ///
    /// # Errors
    ///
    /// Returns a configuration error for invalid endpoint or certificate settings.
    pub fn validate(&self) -> CommResult<()> {
        if !self.endpoint.starts_with("opc.tcp://") {
            return Err(configuration("endpoint must start with opc.tcp://"));
        }
        if self.security_mode != MessageSecurityMode::None
            && (self.certificate.is_none()
                || self.private_key.is_none()
                || self.trust_list.is_empty())
        {
            return Err(configuration(
                "secure OPC UA requires certificate, private key and trust list",
            ));
        }
        if self.security_mode == MessageSecurityMode::None
            && self.security_policy != SecurityPolicy::None
        {
            return Err(configuration(
                "security policy requires Sign or SignAndEncrypt",
            ));
        }
        Ok(())
    }
}

/// One namespace entry returned from Browse.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BrowseNode {
    /// `NodeId` text.
    pub node_id: String,
    /// Browse name.
    pub browse_name: String,
    /// Human-readable display name.
    pub display_name: String,
    /// Whether the node can hold a value.
    pub variable: bool,
}

/// Backend contract implemented by native code and the deterministic simulator.
#[async_trait]
pub trait OpcUaBackend: Send + Sync {
    /// Opens a session.
    async fn connect(&self, options: &OpcUaOptions) -> CommResult<()>;
    /// Closes a session.
    async fn disconnect(&self) -> CommResult<()>;
    /// Reads a Value attribute.
    async fn read_value(&self, node_id: &str) -> CommResult<DeviceValue>;
    /// Writes a Value attribute.
    async fn write_value(&self, node_id: &str, value: &DeviceValue) -> CommResult<()>;
    /// Browses hierarchical children.
    async fn browse(&self, node_id: &str) -> CommResult<Vec<BrowseNode>>;
    /// Adds a native or simulated monitored item.
    async fn monitor(
        &self,
        node_id: &str,
        sampling_interval: std::time::Duration,
    ) -> CommResult<()>;
    /// Receives data-change notifications.
    fn subscribe(&self) -> broadcast::Receiver<(String, DeviceValue)>;
    /// Reports backend-specific native features.
    fn capabilities(&self) -> DeviceCapabilities {
        DeviceCapabilities::READ_WRITE
    }
}

/// Safe OPC UA client used through the common `DeviceClient` interface.
pub struct OpcUaClient<B: OpcUaBackend> {
    options: OpcUaOptions,
    backend: Arc<B>,
    status_tx: watch::Sender<DeviceStatus>,
}

impl<B: OpcUaBackend> OpcUaClient<B> {
    /// Creates a disconnected client.
    ///
    /// # Errors
    ///
    /// Returns a configuration error when `options` are inconsistent.
    pub fn new(options: OpcUaOptions, backend: Arc<B>) -> CommResult<Self> {
        options.validate()?;
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Ok(Self {
            options,
            backend,
            status_tx,
        })
    }
    /// Browses one node.
    ///
    /// # Errors
    ///
    /// Returns backend connection, service, or node errors.
    pub async fn browse(&self, node_id: &str) -> CommResult<Vec<BrowseNode>> {
        self.backend.browse(node_id).await
    }
    /// Creates a monitored item that publishes through [`Self::subscribe`].
    ///
    /// # Errors
    ///
    /// Returns backend connection, service, or node errors.
    pub async fn monitor(
        &self,
        node_id: &str,
        sampling_interval: std::time::Duration,
    ) -> CommResult<()> {
        self.backend.monitor(node_id, sampling_interval).await
    }
    /// Subscribes to data changes.
    pub fn subscribe(&self) -> broadcast::Receiver<(String, DeviceValue)> {
        self.backend.subscribe()
    }
    fn publish(&self, state: ConnectionState, error: Option<&CommError>) {
        self.status_tx.send_replace(DeviceStatus {
            state,
            changed_at: SystemTime::now(),
            last_error_code: error.map(|error| error.code.clone()),
            reconnect_attempt: 0,
        });
    }
}

#[async_trait]
impl<B: OpcUaBackend + 'static> DeviceClient for OpcUaClient<B> {
    async fn connect(&self) -> CommResult<()> {
        self.publish(ConnectionState::Connecting, None);
        let result = self.backend.connect(&self.options).await;
        self.publish(
            if result.is_ok() {
                ConnectionState::Connected
            } else {
                ConnectionState::Faulted
            },
            result.as_ref().err(),
        );
        result
    }
    async fn disconnect(&self) -> CommResult<()> {
        let result = self.backend.disconnect().await;
        self.publish(ConnectionState::Disconnected, result.as_ref().err());
        result
    }
    async fn read(&self, request: &ReadRequest) -> CommResult<DeviceValue> {
        self.backend.read_value(&request.address).await
    }
    async fn write(&self, request: &WriteRequest) -> CommResult<()> {
        self.backend
            .write_value(&request.address, &request.value)
            .await
    }
    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
    fn capabilities(&self) -> DeviceCapabilities {
        self.backend.capabilities()
    }
}

/// Deterministic backend used by simulator CI without external servers.
pub struct SimulatorBackend {
    connected: AtomicBool,
    values: RwLock<HashMap<String, DeviceValue>>,
    nodes: RwLock<HashMap<String, Vec<BrowseNode>>>,
    updates: broadcast::Sender<(String, DeviceValue)>,
}

impl Default for SimulatorBackend {
    fn default() -> Self {
        let (updates, _) = broadcast::channel(256);
        Self {
            connected: AtomicBool::new(false),
            values: RwLock::new(HashMap::new()),
            nodes: RwLock::new(HashMap::new()),
            updates,
        }
    }
}

impl SimulatorBackend {
    /// Inserts a variable and makes it visible below its parent.
    pub async fn insert(&self, parent: &str, node_id: &str, browse_name: &str, value: DeviceValue) {
        self.values.write().await.insert(node_id.to_owned(), value);
        self.nodes
            .write()
            .await
            .entry(parent.to_owned())
            .or_default()
            .push(BrowseNode {
                node_id: node_id.to_owned(),
                browse_name: browse_name.to_owned(),
                display_name: browse_name.to_owned(),
                variable: true,
            });
    }
    fn ensure_connected(&self) -> CommResult<()> {
        if self.connected.load(Ordering::Acquire) {
            Ok(())
        } else {
            Err(CommError::new(
                "OPCUA.UNAVAILABLE.DISCONNECTED",
                CommErrorCategory::Unavailable,
                "OPC UA session is disconnected",
                true,
            ))
        }
    }
}

#[async_trait]
impl OpcUaBackend for SimulatorBackend {
    async fn connect(&self, _: &OpcUaOptions) -> CommResult<()> {
        self.connected.store(true, Ordering::Release);
        Ok(())
    }
    async fn disconnect(&self) -> CommResult<()> {
        self.connected.store(false, Ordering::Release);
        Ok(())
    }
    async fn read_value(&self, node_id: &str) -> CommResult<DeviceValue> {
        self.ensure_connected()?;
        self.values
            .read()
            .await
            .get(node_id)
            .cloned()
            .ok_or_else(node_not_found)
    }
    async fn write_value(&self, node_id: &str, value: &DeviceValue) -> CommResult<()> {
        self.ensure_connected()?;
        let mut values = self.values.write().await;
        if !values.contains_key(node_id) {
            return Err(node_not_found());
        }
        values.insert(node_id.to_owned(), value.clone());
        let _ = self.updates.send((node_id.to_owned(), value.clone()));
        Ok(())
    }
    async fn browse(&self, node_id: &str) -> CommResult<Vec<BrowseNode>> {
        self.ensure_connected()?;
        Ok(self
            .nodes
            .read()
            .await
            .get(node_id)
            .cloned()
            .unwrap_or_default())
    }

    async fn monitor(&self, node_id: &str, _: std::time::Duration) -> CommResult<()> {
        self.ensure_connected()?;
        if self.values.read().await.contains_key(node_id) {
            Ok(())
        } else {
            Err(node_not_found())
        }
    }
    fn subscribe(&self) -> broadcast::Receiver<(String, DeviceValue)> {
        self.updates.subscribe()
    }

    fn capabilities(&self) -> DeviceCapabilities {
        DeviceCapabilities {
            browse: true,
            secure_channel: true,
            ..DeviceCapabilities::READ_WRITE
        }
    }
}

fn configuration(message: impl Into<String>) -> CommError {
    CommError::new(
        "OPCUA.CONFIGURATION.INVALID",
        CommErrorCategory::Configuration,
        message,
        false,
    )
}
fn node_not_found() -> CommError {
    CommError::new(
        "OPCUA.NODE.NOT_FOUND",
        CommErrorCategory::DeviceRejected,
        "OPC UA node was not found",
        false,
    )
}

#[cfg(feature = "native")]
mod native_backend {
    use super::*;
    use aurora_opcua_sys::{NativeClient, NativeScalar};
    use std::sync::Mutex as StdMutex;
    use tokio::sync::Mutex;

    /// open62541-backed scalar read/write and secure-session backend.
    pub struct Open62541Backend {
        client: Arc<StdMutex<Option<NativeClient>>>,
        updates: broadcast::Sender<(String, DeviceValue)>,
        running: Arc<AtomicBool>,
        worker: Mutex<Option<tokio::task::JoinHandle<()>>>,
    }

    impl Default for Open62541Backend {
        fn default() -> Self {
            let (updates, _) = broadcast::channel(16);
            Self {
                client: Arc::new(StdMutex::new(None)),
                updates,
                running: Arc::new(AtomicBool::new(false)),
                worker: Mutex::new(None),
            }
        }
    }

    impl Drop for Open62541Backend {
        fn drop(&mut self) {
            self.running.store(false, Ordering::Release);
        }
    }

    #[async_trait]
    impl OpcUaBackend for Open62541Backend {
        async fn connect(&self, options: &OpcUaOptions) -> CommResult<()> {
            options.validate()?;
            if self.client.lock().map_err(|_| poisoned())?.is_some() {
                return Ok(());
            }
            let credentials = options
                .username
                .as_deref()
                .map(|username| (username, options.password.as_deref().unwrap_or("")));
            let certificate = options.certificate.as_deref().unwrap_or_default();
            let private_key = options.private_key.as_deref().unwrap_or_default();
            let security_mode = match options.security_mode {
                MessageSecurityMode::None => 1,
                MessageSecurityMode::Sign => 2,
                MessageSecurityMode::SignAndEncrypt => 3,
            };
            let security_policy = match options.security_policy {
                SecurityPolicy::None => "http://opcfoundation.org/UA/SecurityPolicy#None",
                SecurityPolicy::Basic256Sha256 => {
                    "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"
                }
                SecurityPolicy::Aes128Sha256RsaOaep => {
                    "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep"
                }
                SecurityPolicy::Aes256Sha256RsaPss => {
                    "http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss"
                }
            };
            let client = NativeClient::connect_configured(
                &options.endpoint,
                certificate,
                private_key,
                &options.trust_list,
                security_mode,
                security_policy,
                credentials,
            )
            .map_err(native_error)?;
            *self.client.lock().map_err(|_| poisoned())? = Some(client);
            self.running.store(true, Ordering::Release);
            let native = Arc::clone(&self.client);
            let running = Arc::clone(&self.running);
            let updates = self.updates.clone();
            *self.worker.lock().await = Some(tokio::task::spawn_blocking(move || {
                while running.load(Ordering::Acquire) {
                    let event = native
                        .lock()
                        .ok()
                        .and_then(|guard| guard.as_ref().and_then(|client| client.iterate(50).ok()))
                        .flatten();
                    if let Some(event) = event {
                        if let Ok(value) = decode(event.value) {
                            let _ = updates.send((event.node_id, value));
                        }
                    }
                }
            }));
            Ok(())
        }

        async fn disconnect(&self) -> CommResult<()> {
            self.running.store(false, Ordering::Release);
            if let Some(worker) = self.worker.lock().await.take() {
                worker.await.map_err(|error| {
                    CommError::new(
                        "OPCUA.NATIVE.WORKER",
                        CommErrorCategory::Internal,
                        format!("OPC UA iterate worker failed: {error}"),
                        false,
                    )
                })?;
            }
            self.client.lock().map_err(|_| poisoned())?.take();
            Ok(())
        }

        async fn read_value(&self, node_id: &str) -> CommResult<DeviceValue> {
            let guard = self.client.lock().map_err(|_| poisoned())?;
            let value = guard
                .as_ref()
                .ok_or_else(disconnected)?
                .read(node_id)
                .map_err(native_error)?;
            decode(value)
        }

        async fn write_value(&self, node_id: &str, value: &DeviceValue) -> CommResult<()> {
            let scalar = encode(value)?;
            let guard = self.client.lock().map_err(|_| poisoned())?;
            guard
                .as_ref()
                .ok_or_else(disconnected)?
                .write(node_id, scalar)
                .map_err(native_error)
        }

        async fn browse(&self, node_id: &str) -> CommResult<Vec<BrowseNode>> {
            let guard = self.client.lock().map_err(|_| poisoned())?;
            let nodes = guard
                .as_ref()
                .ok_or_else(disconnected)?
                .browse(node_id, 4096)
                .map_err(native_error)?;
            Ok(nodes
                .into_iter()
                .map(|node| BrowseNode {
                    node_id: node.node_id,
                    browse_name: node.browse_name,
                    display_name: node.display_name,
                    variable: node.variable,
                })
                .collect())
        }

        async fn monitor(
            &self,
            node_id: &str,
            sampling_interval: std::time::Duration,
        ) -> CommResult<()> {
            let milliseconds = sampling_interval.as_secs_f64() * 1000.0;
            let guard = self.client.lock().map_err(|_| poisoned())?;
            guard
                .as_ref()
                .ok_or_else(disconnected)?
                .subscribe(node_id, milliseconds)
                .map_err(native_error)
        }

        fn subscribe(&self) -> broadcast::Receiver<(String, DeviceValue)> {
            self.updates.subscribe()
        }

        fn capabilities(&self) -> DeviceCapabilities {
            DeviceCapabilities {
                browse: true,
                watch: true,
                secure_channel: true,
                ..DeviceCapabilities::READ_WRITE
            }
        }
    }

    fn disconnected() -> CommError {
        CommError::new(
            "OPCUA.UNAVAILABLE.DISCONNECTED",
            CommErrorCategory::Unavailable,
            "OPC UA session is disconnected",
            true,
        )
    }
    fn poisoned() -> CommError {
        CommError::new(
            "OPCUA.NATIVE.LOCK_POISONED",
            CommErrorCategory::Internal,
            "OPC UA native client lock was poisoned",
            false,
        )
    }
    fn native_error(code: u32) -> CommError {
        let (stable_code, category, retryable) = match code {
            0x800A_0000 | 0x8085_0000 => ("OPCUA.NATIVE.TIMEOUT", CommErrorCategory::Timeout, true),
            0x8005_0000 | 0x800C_0000 | 0x800D_0000 | 0x8026_0000 | 0x8086_0000 | 0x808A_0000
            | 0x80AC_0000 | 0x80AE_0000 => {
                ("OPCUA.NATIVE.TRANSPORT", CommErrorCategory::Transport, true)
            }
            0x8013_0000 | 0x8016_0000 | 0x8017_0000 | 0x8018_0000 | 0x801A_0000 | 0x801D_0000
            | 0x801F_0000 | 0x8021_0000 | 0x8054_0000 | 0x8055_0000 => (
                "OPCUA.NATIVE.AUTHENTICATION",
                CommErrorCategory::Authentication,
                false,
            ),
            _ => ("OPCUA.NATIVE.STATUS", CommErrorCategory::Protocol, false),
        };
        CommError::new(
            stable_code,
            category,
            format!("open62541 status 0x{code:08X}"),
            retryable,
        )
        .with_protocol_code(code as i32)
    }
    fn decode(value: NativeScalar) -> CommResult<DeviceValue> {
        let d = value.data;
        Ok(match value.kind {
            0 => DeviceValue::Bool(d[0] != 0),
            1 => DeviceValue::UInt16(u16::from_ne_bytes(d[..2].try_into().unwrap())),
            2 => DeviceValue::Int16(i16::from_ne_bytes(d[..2].try_into().unwrap())),
            3 => DeviceValue::UInt32(u32::from_ne_bytes(d[..4].try_into().unwrap())),
            4 => DeviceValue::Int32(i32::from_ne_bytes(d[..4].try_into().unwrap())),
            5 => DeviceValue::UInt64(u64::from_ne_bytes(d[..8].try_into().unwrap())),
            6 => DeviceValue::Int64(i64::from_ne_bytes(d[..8].try_into().unwrap())),
            7 => DeviceValue::Float32(f32::from_ne_bytes(d[..4].try_into().unwrap())),
            8 => DeviceValue::Float64(f64::from_ne_bytes(d[..8].try_into().unwrap())),
            _ => return Err(native_error(0x8074_0000)),
        })
    }
    fn encode(value: &DeviceValue) -> CommResult<NativeScalar> {
        let (kind, bytes) = match value {
            DeviceValue::Bool(v) => (0, vec![u8::from(*v)]),
            DeviceValue::UInt16(v) => (1, v.to_ne_bytes().to_vec()),
            DeviceValue::Int16(v) => (2, v.to_ne_bytes().to_vec()),
            DeviceValue::UInt32(v) => (3, v.to_ne_bytes().to_vec()),
            DeviceValue::Int32(v) => (4, v.to_ne_bytes().to_vec()),
            DeviceValue::UInt64(v) => (5, v.to_ne_bytes().to_vec()),
            DeviceValue::Int64(v) => (6, v.to_ne_bytes().to_vec()),
            DeviceValue::Float32(v) => (7, v.to_ne_bytes().to_vec()),
            DeviceValue::Float64(v) => (8, v.to_ne_bytes().to_vec()),
            _ => {
                return Err(configuration(
                    "native OPC UA currently accepts scalar numeric values",
                ));
            }
        };
        let mut data = [0; 16];
        data[..bytes.len()].copy_from_slice(&bytes);
        Ok(NativeScalar {
            kind,
            data,
            length: bytes.len() as u32,
        })
    }
}

#[cfg(feature = "native")]
pub use native_backend::Open62541Backend;

#[cfg(test)]
mod tests {
    use super::*;
    use aurora_comm_core::DeviceDataType;
    use std::time::Duration;

    #[tokio::test]
    async fn simulator_covers_browse_read_write_and_subscription() {
        let backend = Arc::new(SimulatorBackend::default());
        backend
            .insert("i=85", "ns=2;s=Speed", "Speed", DeviceValue::Float32(1.5))
            .await;
        let client =
            OpcUaClient::new(OpcUaOptions::anonymous("opc.tcp://localhost:4840"), backend).unwrap();
        client.connect().await.unwrap();
        assert_eq!(client.browse("i=85").await.unwrap().len(), 1);
        client
            .monitor("ns=2;s=Speed", Duration::from_millis(100))
            .await
            .unwrap();
        assert_eq!(
            client
                .read(&ReadRequest {
                    key: "speed".into(),
                    address: "ns=2;s=Speed".into(),
                    data_type: DeviceDataType::Float32,
                    count: 1,
                    timeout: Duration::from_secs(1)
                })
                .await
                .unwrap(),
            DeviceValue::Float32(1.5)
        );
        let mut updates = client.subscribe();
        client
            .write(&WriteRequest {
                key: "speed".into(),
                address: "ns=2;s=Speed".into(),
                value: DeviceValue::Float32(2.0),
                timeout: Duration::from_secs(1),
            })
            .await
            .unwrap();
        assert_eq!(updates.recv().await.unwrap().1, DeviceValue::Float32(2.0));
    }
}
