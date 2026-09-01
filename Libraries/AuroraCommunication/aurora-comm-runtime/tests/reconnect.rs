//! Managed-session disconnect recovery acceptance tests.

use async_trait::async_trait;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DeviceClient, DeviceDataType,
    DeviceStatus, DeviceValue, OperationPriority, ReadRequest, WriteRequest,
};
use aurora_comm_runtime::{ManagedSession, ManagedSessionOptions};
use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::{Duration, SystemTime};
use tokio::sync::watch;

struct RecoveringClient {
    connects: AtomicUsize,
    reads: AtomicUsize,
    disconnects: AtomicUsize,
    status_tx: watch::Sender<DeviceStatus>,
}

impl RecoveringClient {
    fn new() -> Self {
        let (status_tx, _) = watch::channel(DeviceStatus::disconnected());
        Self {
            connects: AtomicUsize::new(0),
            reads: AtomicUsize::new(0),
            disconnects: AtomicUsize::new(0),
            status_tx,
        }
    }

    fn publish(&self, state: ConnectionState) {
        self.status_tx.send_replace(DeviceStatus {
            state,
            changed_at: SystemTime::now(),
            last_error_code: None,
            reconnect_attempt: u32::try_from(self.connects.load(Ordering::SeqCst))
                .unwrap_or(u32::MAX),
        });
    }
}

#[async_trait]
impl DeviceClient for RecoveringClient {
    async fn connect(&self) -> CommResult<()> {
        self.connects.fetch_add(1, Ordering::SeqCst);
        self.publish(ConnectionState::Connected);
        Ok(())
    }

    async fn disconnect(&self) -> CommResult<()> {
        self.disconnects.fetch_add(1, Ordering::SeqCst);
        self.publish(ConnectionState::Disconnected);
        Ok(())
    }

    async fn read(&self, _: &ReadRequest) -> CommResult<DeviceValue> {
        if self.reads.fetch_add(1, Ordering::SeqCst) == 0 {
            return Err(CommError::new(
                "TEST.TRANSPORT.DROPPED",
                CommErrorCategory::Transport,
                "simulated cable disconnect",
                true,
            ));
        }
        Ok(DeviceValue::UInt16(42))
    }

    async fn write(&self, _: &WriteRequest) -> CommResult<()> {
        Ok(())
    }

    fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_tx.subscribe()
    }
}

#[tokio::test]
async fn transport_failure_disconnects_and_next_command_reconnects() {
    let client = Arc::new(RecoveringClient::new());
    let session = ManagedSession::spawn(
        Arc::clone(&client) as Arc<dyn DeviceClient>,
        ManagedSessionOptions {
            reconnect_initial: Duration::from_millis(1),
            reconnect_max: Duration::from_millis(2),
            ..ManagedSessionOptions::default()
        },
    );
    let request = ReadRequest {
        key: "reconnect".to_owned(),
        address: "simulated".to_owned(),
        data_type: DeviceDataType::UInt16,
        count: 1,
        timeout: Duration::from_secs(1),
    };

    let first = session
        .read(request.clone(), OperationPriority::Control)
        .await;
    assert!(first.is_err());
    assert_eq!(
        session.status().borrow().state,
        ConnectionState::Disconnected
    );

    let recovered = session
        .read(request, OperationPriority::Control)
        .await
        .expect("next command should reconnect");
    assert_eq!(recovered, DeviceValue::UInt16(42));
    assert_eq!(client.connects.load(Ordering::SeqCst), 2);
    assert_eq!(client.disconnects.load(Ordering::SeqCst), 1);
    session.shutdown();
}
