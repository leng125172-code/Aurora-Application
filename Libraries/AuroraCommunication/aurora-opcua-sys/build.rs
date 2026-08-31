//! Verifies and compiles the pinned native OPC UA dependencies when enabled.

use sha2::{Digest, Sha256};
use std::{
    env, fs,
    io::Read,
    path::{Path, PathBuf},
};

const OPEN_C_SHA256: &str = "9768d3f019ff67be0f9a815d0acd09a9e1906b0dcbb373512343aa41feecd87f";
const OPEN_H_SHA256: &str = "b6edbc3b2d213927750af9774adc13f3bb9bbfa52199aa46f9e57cf657186afe";
const MBED_SHA256: &str = "a7e8bcbec0e6f761b4af24f25677626b35f762f68eef79c08677a363212d11f6";

fn main() {
    println!("cargo:rerun-if-env-changed=CARGO_FEATURE_NATIVE");
    if env::var_os("CARGO_FEATURE_NATIVE").is_none() {
        return;
    }
    let root =
        PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").unwrap()).join("../third-party/opcua");
    let open = root.join("open62541-1.5.4");
    let archive = root.join("mbedtls-3.6.7/mbedtls-3.6.7.tar.bz2");
    verify(&open.join("open62541.c"), OPEN_C_SHA256);
    verify(&open.join("open62541.h"), OPEN_H_SHA256);
    verify(&archive, MBED_SHA256);

    let out = PathBuf::from(env::var_os("OUT_DIR").unwrap());
    let extracted = out.join("vendor");
    if !extracted.join("mbedtls-3.6.7/CMakeLists.txt").exists() {
        let _ = fs::remove_dir_all(&extracted);
        fs::create_dir_all(&extracted).unwrap();
        let input = fs::File::open(&archive).unwrap();
        let decoder = bzip2::read::BzDecoder::new(input);
        tar::Archive::new(decoder).unpack(&extracted).unwrap();
    }
    let mbed = cmake::Config::new(extracted.join("mbedtls-3.6.7"))
        .define("ENABLE_PROGRAMS", "OFF")
        .define("ENABLE_TESTING", "OFF")
        .define("USE_SHARED_MBEDTLS_LIBRARY", "OFF")
        .build();
    let include = mbed.join("include");
    let generated_open = out.join("open62541");
    fs::create_dir_all(&generated_open).unwrap();
    fs::copy(open.join("open62541.c"), generated_open.join("open62541.c")).unwrap();
    let header = fs::read_to_string(open.join("open62541.h")).unwrap().replace(
        "/* #undef UA_ARCHITECTURE_WIN32 */\n#define UA_ARCHITECTURE_POSIX",
        "#ifdef _WIN32\n#define UA_ARCHITECTURE_WIN32\n#else\n#define UA_ARCHITECTURE_POSIX\n#endif",
    );
    fs::write(generated_open.join("open62541.h"), header).unwrap();
    cc::Build::new()
        .file(generated_open.join("open62541.c"))
        .file("src/wrapper.c")
        .include(&generated_open)
        .include(&include)
        .warnings(false)
        .compile("aurora_open62541");
    println!(
        "cargo:rustc-link-search=native={}",
        mbed.join("lib").display()
    );
    for name in ["mbedtls", "mbedx509", "mbedcrypto"] {
        println!("cargo:rustc-link-lib=static={name}");
    }
    if env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        for name in ["ws2_32", "iphlpapi", "bcrypt", "crypt32"] {
            println!("cargo:rustc-link-lib={name}");
        }
    }
}

fn verify(path: &Path, expected: &str) {
    println!("cargo:rerun-if-changed={}", path.display());
    let mut file = fs::File::open(path)
        .unwrap_or_else(|e| panic!("missing vendored file {}: {e}", path.display()));
    let mut bytes = Vec::new();
    file.read_to_end(&mut bytes).unwrap();
    let actual = format!("{:x}", Sha256::digest(&bytes));
    assert_eq!(
        actual,
        expected,
        "vendored dependency checksum mismatch: {}",
        path.display()
    );
}
