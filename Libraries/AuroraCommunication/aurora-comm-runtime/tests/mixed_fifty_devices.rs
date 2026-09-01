//! Mixed-protocol scheduling acceptance test for the 50-device design target.

use async_trait::async_trait;
use aurora_comm_core::{
    CommResult, ConnectionState, DeviceClient, DeviceDataType, DeviceStatus, DeviceValue,
    OperationPriority, ReadRequest, WriteRequest,
};
use aurora_comm_runtime::{ManagedSession, ManagedSessionOptions};
use std::{
    sync::Arc,
    time::{Duration, Instant, SystemTime},
};
use tokio::{sync::watch, task::JoinSet};

const DEVICE_COUNT: usize = 50;
const NORMAL_LOAD_PER_DEVICE: usize = 12;

struct SimulatedProtocolClient {
    protocol: &'static str,
    status_tx: watch::Sender<DeviceStatus>,
}

impl SimulatedProtocolClient {
    fn new(protocol: &'static str) -> Self {
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Self {
            protocol,
            status_tx,
        }
    }
}

#[async_trait]
impl DeviceClient for SimulatedProtocolClient {
    async fn connect(&self) -> CommResult<()> {
        self.status_tx.send_replace(DeviceStatus {
            state: ConnectionState::Connected,
            changed_at: SystemTime::now(),
            last_error_code: None,
            reconnect_attempt: 0,
        });
        Ok(())
    }

    async fn disconnect(&self) -> CommResult<()> {
        self.status_tx.send_replace(DeviceStatus::disconnected());
        Ok(())
    }

    async fn read(&self, _: &ReadRequest) -> CommResult<DeviceValue> {
        tokio::time::sleep(Duration::from_millis(2)).await;
        Ok(DeviceValue::String(self.protocol.to_owned()))
    }

    async fn write(&self, _: &WriteRequest) -> CommResult<()> {
        tokio::time::sleep(Duration::from_millis(1)).await;
        Ok(())
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}

#[tokio::test(flavor = "multi_thread", worker_threads = 8)]
async fn mixed_fifty_devices_keep_control_p99_below_100ms() {
    let protocols = ["modbus", "s7", "mc3e", "opcua"];
    let sessions = (0..DEVICE_COUNT)
        .map(|index| {
            let client: Arc<dyn DeviceClient> = Arc::new(SimulatedProtocolClient::new(
                protocols[index % protocols.len()],
            ));
            ManagedSession::spawn(client, ManagedSessionOptions::default())
        })
        .collect::<Vec<_>>();

    let mut normal_load = JoinSet::new();
    for session in &sessions {
        for index in 0..NORMAL_LOAD_PER_DEVICE {
            let session = session.clone();
            normal_load.spawn(async move {
                session
                    .read(
                        ReadRequest {
                            key: format!("sample-{index}"),
                            address: "simulated".to_owned(),
                            data_type: DeviceDataType::String,
                            count: 1,
                            timeout: Duration::from_secs(1),
                        },
                        OperationPriority::Normal,
                    )
                    .await
            });
        }
    }

    tokio::task::yield_now().await;
    let mut controls = JoinSet::new();
    for session in &sessions {
        let session = session.clone();
        controls.spawn(async move {
            let started = Instant::now();
            session
                .write(
                    WriteRequest {
                        key: "control".to_owned(),
                        address: "simulated".to_owned(),
                        value: DeviceValue::Bool(true),
                        timeout: Duration::from_secs(1),
                    },
                    OperationPriority::Control,
                )
                .await?;
            Ok::<_, aurora_comm_core::CommError>(started.elapsed())
        });
    }

    let mut latencies = Vec::with_capacity(DEVICE_COUNT);
    while let Some(result) = controls.join_next().await {
        latencies.push(result.unwrap().unwrap());
    }
    latencies.sort_unstable();
    let p99 = latencies[(latencies.len() * 99).div_ceil(100) - 1];
    assert!(p99 < Duration::from_millis(100), "control P99 was {p99:?}");

    while let Some(result) = normal_load.join_next().await {
        result.unwrap().unwrap();
    }
    for session in sessions {
        session.shutdown();
    }
}
