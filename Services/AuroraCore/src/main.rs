//! Aurora 2.0 control-core service executable.

use anyhow::{Context, Result};
use aurora_core_service::actor::StationHandle;
use aurora_core_service::api::StationControlApi;
use aurora_core_service::contracts::station_control_server::StationControlServer;
use aurora_core_service::lease::LeaseManager;
use aurora_core_service::persistence::EventJournal;
use std::net::SocketAddr;
use std::sync::Arc;
use tonic::transport::Server;
use tracing::info;
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .init();

    let address = std::env::var("AURORA_CORE_GRPC")
        .unwrap_or_else(|_| "127.0.0.1:50051".to_owned())
        .parse::<SocketAddr>()
        .context("parse AURORA_CORE_GRPC")?;
    if !address.ip().is_loopback() {
        anyhow::bail!("AURORA_CORE_GRPC must remain loopback-only; expose the HMI Host instead");
    }

    let data_path = std::env::var("AURORA_DATA_PATH")
        .unwrap_or_else(|_| "Data/Aurora2/station-outbox.redb".to_owned());
    let postgres_url = std::env::var("AURORA_POSTGRES_URL").ok();
    let journal = Arc::new(EventJournal::open(data_path, postgres_url.as_deref()).await?);
    let projected = journal.flush_pending().await?;
    info!(
        pending_outbox = journal.pending_count()?,
        projected, "event journal ready"
    );

    let station = StationHandle::spawn("main", journal);
    let api = StationControlApi::new(station, LeaseManager::default());
    info!(%address, "Aurora Core gRPC listening");

    Server::builder()
        .add_service(StationControlServer::new(api))
        .serve_with_shutdown(address, shutdown_signal())
        .await?;
    Ok(())
}

async fn shutdown_signal() {
    if let Err(error) = tokio::signal::ctrl_c().await {
        tracing::error!(%error, "failed to install shutdown signal");
    }
    info!("Aurora Core shutdown requested; station outputs must remain hardware-safe");
}
