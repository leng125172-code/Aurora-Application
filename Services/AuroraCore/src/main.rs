//! Aurora 2.0 control-core service executable.

use anyhow::{Context, Result};
use aurora_core_service::actor::StationHandle;
use aurora_core_service::api::StationControlApi;
use aurora_core_service::contracts::device_gateway_server::DeviceGatewayServer;
use aurora_core_service::contracts::station_control_server::StationControlServer;
use aurora_core_service::contracts::vision_gateway_server::VisionGatewayServer;
use aurora_core_service::device_vision_api::{DeviceGatewayProxy, VisionGatewayProxy};
use aurora_core_service::lease::LeaseManager;
use aurora_core_service::persistence::EventJournal;
use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;
use tokio::time::MissedTickBehavior;
use tonic::transport::{Endpoint, Server};
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
    spawn_projection_worker(Arc::clone(&journal));

    let station = StationHandle::spawn("main", journal)?;
    let api = StationControlApi::new(station, LeaseManager::default());
    let device_vision_address = std::env::var("AURORA_DEVICE_VISION_GRPC")
        .unwrap_or_else(|_| "http://127.0.0.1:50052".to_owned());
    let device_vision_channel = local_endpoint(&device_vision_address)?.connect_lazy();
    info!(%address, "Aurora Core gRPC listening");

    Server::builder()
        .add_service(StationControlServer::new(api))
        .add_service(DeviceGatewayServer::new(DeviceGatewayProxy::new(
            device_vision_channel.clone(),
        )))
        .add_service(VisionGatewayServer::new(VisionGatewayProxy::new(
            device_vision_channel,
        )))
        .serve_with_shutdown(address, shutdown_signal())
        .await?;
    Ok(())
}

fn local_endpoint(value: &str) -> Result<Endpoint> {
    let authority = value
        .strip_prefix("http://")
        .or_else(|| value.strip_prefix("https://"))
        .and_then(|remainder| remainder.split('/').next())
        .context("AURORA_DEVICE_VISION_GRPC must be an absolute HTTP(S) URI")?;
    let address = authority
        .parse::<SocketAddr>()
        .context("AURORA_DEVICE_VISION_GRPC must use a literal IP address and port")?;
    if !address.ip().is_loopback() {
        anyhow::bail!("AURORA_DEVICE_VISION_GRPC must remain loopback-only");
    }
    Endpoint::from_shared(value.to_owned()).context("parse AURORA_DEVICE_VISION_GRPC")
}

fn spawn_projection_worker(journal: Arc<EventJournal>) {
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(Duration::from_secs(5));
        interval.set_missed_tick_behavior(MissedTickBehavior::Skip);
        interval.tick().await;
        loop {
            interval.tick().await;
            match journal.flush_pending().await {
                Ok(projected) if projected > 0 => {
                    tracing::info!(projected, "station outbox projected");
                }
                Ok(_) => {}
                Err(error) => {
                    tracing::warn!(%error, "station outbox projection retry failed");
                }
            }
        }
    });
}

async fn shutdown_signal() {
    if let Err(error) = tokio::signal::ctrl_c().await {
        tracing::error!(%error, "failed to install shutdown signal");
    }
    info!("Aurora Core shutdown requested; station outputs must remain hardware-safe");
}
