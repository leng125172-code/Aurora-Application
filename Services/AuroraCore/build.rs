//! Generates the Rust gRPC boundary from the shared Aurora 2.0 contract.

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let protos = [
        "../../Contracts/Protos/aurora/v2/core.proto",
        "../../Contracts/Protos/aurora/v2/device_vision.proto",
    ];
    let include = "../../Contracts/Protos";
    let mut prost = prost_build::Config::new();
    prost.protoc_executable(protoc_bin_vendored::protoc_bin_path()?);
    tonic_prost_build::configure().compile_with_config(prost, &protos, &[include])?;
    for proto in protos {
        println!("cargo:rerun-if-changed={proto}");
    }
    Ok(())
}
