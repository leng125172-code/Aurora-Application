//! Safe ownership wrapper around the private open62541 C boundary.

#[cfg(feature = "native")]
mod native {
    use std::ffi::{CStr, CString, c_char, c_void};

    #[derive(Clone, Copy)]
    #[repr(C)]
    struct Scalar {
        kind: u32,
        length: u32,
        data: [u8; 16],
    }
    #[repr(C)]
    struct BrowseNode {
        node_id: [c_char; 512],
        browse_name: [c_char; 256],
        display_name: [c_char; 256],
        variable: u8,
    }
    impl Default for BrowseNode {
        fn default() -> Self {
            Self {
                node_id: [0; 512],
                browse_name: [0; 256],
                display_name: [0; 256],
                variable: 0,
            }
        }
    }
    #[repr(C)]
    struct Event {
        node_id: [c_char; 512],
        value: Scalar,
        sequence: u64,
    }
    unsafe extern "C" {
        fn aurora_opc_new(endpoint: *const c_char) -> *mut c_void;
        fn aurora_opc_new_configured(
            endpoint: *const c_char,
            certificate: *const u8,
            certificate_len: usize,
            private_key: *const u8,
            private_key_len: usize,
            trust: *const *const u8,
            trust_lengths: *const usize,
            trust_count: usize,
            security_mode: u32,
            security_policy: *const c_char,
            username: *const c_char,
            password: *const c_char,
        ) -> *mut c_void;
        fn aurora_opc_delete(client: *mut c_void);
        fn aurora_opc_read_scalar(
            client: *mut c_void,
            node: *const c_char,
            out: *mut Scalar,
        ) -> u32;
        fn aurora_opc_write_scalar(
            client: *mut c_void,
            node: *const c_char,
            input: *const Scalar,
        ) -> u32;
        fn aurora_opc_browse(
            client: *mut c_void,
            node: *const c_char,
            output: *mut BrowseNode,
            capacity: usize,
            written: *mut usize,
        ) -> u32;
        fn aurora_opc_subscribe(
            client: *mut c_void,
            node: *const c_char,
            sampling_interval: f64,
        ) -> u32;
        fn aurora_opc_iterate(
            client: *mut c_void,
            timeout: u32,
            event: *mut Event,
            has_event: *mut u8,
        ) -> u32;
    }

    /// Native scalar supported by the narrow ABI.
    #[derive(Debug, Clone, Copy, PartialEq)]
    pub struct NativeScalar {
        /// Type discriminator.
        pub kind: u32,
        /// Little native bytes.
        pub data: [u8; 16],
        /// Used bytes.
        pub length: u32,
    }

    /// Owned result from one Browse reference.
    #[derive(Debug, Clone, PartialEq, Eq)]
    pub struct NativeBrowseNode {
        /// Canonical `NodeId` text.
        pub node_id: String,
        /// Qualified browse-name text.
        pub browse_name: String,
        /// Localized display text selected by the server.
        pub display_name: String,
        /// Whether the target has the Variable node class.
        pub variable: bool,
    }

    /// One native MonitoredItem data-change event.
    #[derive(Debug, Clone, PartialEq)]
    pub struct NativeEvent {
        /// Monitored `NodeId` text.
        pub node_id: String,
        /// Scalar value delivered by open62541.
        pub value: NativeScalar,
        /// Per-item monotonic sequence.
        pub sequence: u64,
    }

    /// Owned open62541 client; all raw pointers remain private.
    pub struct NativeClient {
        raw: *mut c_void,
    }
    unsafe impl Send for NativeClient {}
    impl NativeClient {
        /// Connects a new client.
        pub fn connect(endpoint: &str) -> Result<Self, u32> {
            let endpoint = CString::new(endpoint).map_err(|_| 0x80ab_0000_u32)?;
            let raw = unsafe { aurora_opc_new(endpoint.as_ptr()) };
            if raw.is_null() {
                Err(0x8005_0000)
            } else {
                Ok(Self { raw })
            }
        }
        /// Connects with certificate trust and optional user credentials.
        pub fn connect_configured(
            endpoint: &str,
            certificate: &[u8],
            private_key: &[u8],
            trust: &[Vec<u8>],
            security_mode: u32,
            security_policy: &str,
            credentials: Option<(&str, &str)>,
        ) -> Result<Self, u32> {
            let endpoint = CString::new(endpoint).map_err(|_| 0x80ab_0000_u32)?;
            let username = credentials
                .map(|value| CString::new(value.0))
                .transpose()
                .map_err(|_| 0x80ab_0000_u32)?;
            let password = credentials
                .map(|value| CString::new(value.1))
                .transpose()
                .map_err(|_| 0x80ab_0000_u32)?;
            let security_policy = CString::new(security_policy).map_err(|_| 0x80ab_0000_u32)?;
            let trust_pointers = trust.iter().map(Vec::as_ptr).collect::<Vec<_>>();
            let trust_lengths = trust.iter().map(Vec::len).collect::<Vec<_>>();
            let raw = unsafe {
                aurora_opc_new_configured(
                    endpoint.as_ptr(),
                    certificate.as_ptr(),
                    certificate.len(),
                    private_key.as_ptr(),
                    private_key.len(),
                    trust_pointers.as_ptr(),
                    trust_lengths.as_ptr(),
                    trust.len(),
                    security_mode,
                    security_policy.as_ptr(),
                    username
                        .as_ref()
                        .map_or(std::ptr::null(), |value| value.as_ptr()),
                    password
                        .as_ref()
                        .map_or(std::ptr::null(), |value| value.as_ptr()),
                )
            };
            if raw.is_null() {
                Err(0x8005_0000)
            } else {
                Ok(Self { raw })
            }
        }
        /// Reads one scalar.
        pub fn read(&self, node: &str) -> Result<NativeScalar, u32> {
            let node = CString::new(node).map_err(|_| 0x80ab_0000_u32)?;
            let mut value = Scalar {
                kind: 0,
                length: 0,
                data: [0; 16],
            };
            let code = unsafe { aurora_opc_read_scalar(self.raw, node.as_ptr(), &mut value) };
            if code == 0 {
                Ok(NativeScalar {
                    kind: value.kind,
                    data: value.data,
                    length: value.length,
                })
            } else {
                Err(code)
            }
        }
        /// Writes one scalar.
        pub fn write(&self, node: &str, value: NativeScalar) -> Result<(), u32> {
            let node = CString::new(node).map_err(|_| 0x80ab_0000_u32)?;
            let value = Scalar {
                kind: value.kind,
                length: value.length,
                data: value.data,
            };
            let code = unsafe { aurora_opc_write_scalar(self.raw, node.as_ptr(), &value) };
            if code == 0 { Ok(()) } else { Err(code) }
        }

        /// Browses hierarchical forward references, following continuation points.
        pub fn browse(&self, node: &str, maximum: usize) -> Result<Vec<NativeBrowseNode>, u32> {
            let node = CString::new(node).map_err(|_| 0x80ab_0000_u32)?;
            let mut output = (0..maximum)
                .map(|_| BrowseNode::default())
                .collect::<Vec<_>>();
            let mut written = 0;
            let code = unsafe {
                aurora_opc_browse(
                    self.raw,
                    node.as_ptr(),
                    output.as_mut_ptr(),
                    output.len(),
                    &mut written,
                )
            };
            if code != 0 {
                return Err(code);
            }
            output.truncate(written);
            Ok(output
                .into_iter()
                .map(|value| NativeBrowseNode {
                    node_id: c_text(&value.node_id),
                    browse_name: c_text(&value.browse_name),
                    display_name: c_text(&value.display_name),
                    variable: value.variable != 0,
                })
                .collect())
        }

        /// Creates a native data-change MonitoredItem.
        pub fn subscribe(&self, node: &str, sampling_interval_ms: f64) -> Result<(), u32> {
            let node = CString::new(node).map_err(|_| 0x80ab_0000_u32)?;
            let code =
                unsafe { aurora_opc_subscribe(self.raw, node.as_ptr(), sampling_interval_ms) };
            if code == 0 { Ok(()) } else { Err(code) }
        }

        /// Advances the open62541 event loop and returns at most one pending update.
        pub fn iterate(&self, timeout_ms: u32) -> Result<Option<NativeEvent>, u32> {
            let mut event = Event {
                node_id: [0; 512],
                value: Scalar {
                    kind: 0,
                    length: 0,
                    data: [0; 16],
                },
                sequence: 0,
            };
            let mut has_event = 0;
            let code =
                unsafe { aurora_opc_iterate(self.raw, timeout_ms, &mut event, &mut has_event) };
            if code != 0 {
                return Err(code);
            }
            Ok((has_event != 0).then(|| NativeEvent {
                node_id: c_text(&event.node_id),
                value: NativeScalar {
                    kind: event.value.kind,
                    data: event.value.data,
                    length: event.value.length,
                },
                sequence: event.sequence,
            }))
        }
    }
    impl Drop for NativeClient {
        fn drop(&mut self) {
            unsafe { aurora_opc_delete(self.raw) }
        }
    }

    fn c_text<const N: usize>(value: &[c_char; N]) -> String {
        unsafe { CStr::from_ptr(value.as_ptr()) }
            .to_string_lossy()
            .into_owned()
    }
}

#[cfg(feature = "native")]
pub use native::{NativeBrowseNode, NativeClient, NativeEvent, NativeScalar};

/// Compiled open62541 version.
pub const OPEN62541_VERSION: &str = "1.5.4";
/// Compiled mbedTLS version.
pub const MBEDTLS_VERSION: &str = "3.6.7";
