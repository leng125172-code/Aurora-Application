//! Safe ownership wrapper around the private open62541 C boundary.

#[cfg(feature = "native")]
mod native {
    use std::ffi::{CString, c_char, c_void};

    #[repr(C)]
    struct Scalar {
        kind: u32,
        length: u32,
        data: [u8; 16],
    }
    unsafe extern "C" {
        fn aurora_opc_new(endpoint: *const c_char) -> *mut c_void;
        fn aurora_opc_new_configured(
            endpoint: *const c_char,
            certificate: *const u8,
            certificate_len: usize,
            private_key: *const u8,
            private_key_len: usize,
            trust: *const u8,
            trust_len: usize,
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
            trust: &[u8],
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
            let raw = unsafe {
                aurora_opc_new_configured(
                    endpoint.as_ptr(),
                    certificate.as_ptr(),
                    certificate.len(),
                    private_key.as_ptr(),
                    private_key.len(),
                    trust.as_ptr(),
                    trust.len(),
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
    }
    impl Drop for NativeClient {
        fn drop(&mut self) {
            unsafe { aurora_opc_delete(self.raw) }
        }
    }
}

#[cfg(feature = "native")]
pub use native::{NativeClient, NativeScalar};

/// Compiled open62541 version.
pub const OPEN62541_VERSION: &str = "1.5.4";
/// Compiled mbedTLS version.
pub const MBEDTLS_VERSION: &str = "3.6.7";
