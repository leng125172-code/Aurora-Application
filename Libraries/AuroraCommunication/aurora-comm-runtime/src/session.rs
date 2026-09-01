//! One serialized managed command lane per device.

use crate::WatchSubscription;
use aurora_comm_core::{
    CommError, CommErrorCategory, CommResult, ConnectionState, DeviceClient, DeviceStatus,
    DeviceUpdate, DeviceValue, OperationPriority, ReadRequest, ValueQuality, WatchSpec,
    WriteRequest,
};
use std::collections::HashMap;
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime};
use tokio::sync::{mpsc, oneshot, watch};
use tokio::task::JoinHandle;
use tokio_util::sync::CancellationToken;
use tracing::{debug, warn};
use uuid::Uuid;

/// Queue, reconnect, and fairness settings for a managed session.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ManagedSessionOptions {
    /// Maximum queued safety/control operations.
    pub control_queue_capacity: usize,
    /// Maximum queued ordinary reads and writes.
    pub normal_queue_capacity: usize,
    /// Control operations allowed before servicing a normal operation.
    pub max_control_burst: usize,
    /// Initial reconnect backoff.
    pub reconnect_initial: Duration,
    /// Maximum reconnect backoff.
    pub reconnect_max: Duration,
}

impl Default for ManagedSessionOptions {
    fn default() -> Self {
        Self {
            control_queue_capacity: 256,
            normal_queue_capacity: 1024,
            max_control_burst: 8,
            reconnect_initial: Duration::from_millis(250),
            reconnect_max: Duration::from_secs(30),
        }
    }
}

enum SessionCommand {
    Read {
        request: ReadRequest,
        response: oneshot::Sender<CommResult<DeviceValue>>,
    },
    Write {
        request: WriteRequest,
        response: oneshot::Sender<CommResult<()>>,
    },
}

struct WatchEntry {
    spec: WatchSpec,
    next_due: Instant,
    sender: watch::Sender<DeviceUpdate>,
}

/// Handle to a background task that owns one device protocol session.
#[derive(Clone)]
pub struct ManagedSession {
    control_tx: mpsc::Sender<SessionCommand>,
    normal_tx: mpsc::Sender<SessionCommand>,
    watch_add_tx: mpsc::UnboundedSender<(Uuid, WatchSpec, watch::Sender<DeviceUpdate>)>,
    watch_cancel_tx: mpsc::UnboundedSender<Uuid>,
    status_rx: watch::Receiver<DeviceStatus>,
    shutdown: CancellationToken,
    _worker: Arc<JoinHandle<()>>,
}

impl ManagedSession {
    /// Starts a managed session around a protocol client.
    pub fn spawn(client: Arc<dyn DeviceClient>, options: ManagedSessionOptions) -> Self {
        let (control_tx, control_rx) = mpsc::channel(options.control_queue_capacity);
        let (normal_tx, normal_rx) = mpsc::channel(options.normal_queue_capacity);
        let (watch_add_tx, watch_add_rx) = mpsc::unbounded_channel();
        let (watch_cancel_tx, watch_cancel_rx) = mpsc::unbounded_channel();
        let status_rx = client.status();
        let shutdown = CancellationToken::new();
        let worker_shutdown = shutdown.clone();
        let worker_client = client;
        let worker_options = options;
        let worker = tokio::spawn(async move {
            run_session(
                worker_client,
                worker_options,
                control_rx,
                normal_rx,
                watch_add_rx,
                watch_cancel_rx,
                worker_shutdown,
            )
            .await;
        });

        Self {
            control_tx,
            normal_tx,
            watch_add_tx,
            watch_cancel_tx,
            status_rx,
            shutdown,
            _worker: Arc::new(worker),
        }
    }

    /// Executes a read on the requested priority lane.
    ///
    /// # Errors
    ///
    /// Returns a queue, connection, protocol, or device error.
    pub async fn read(
        &self,
        request: ReadRequest,
        priority: OperationPriority,
    ) -> CommResult<DeviceValue> {
        let (response_tx, response_rx) = oneshot::channel();
        self.sender(priority)
            .try_send(SessionCommand::Read {
                request,
                response: response_tx,
            })
            .map_err(queue_error)?;
        response_rx.await.map_err(|_| worker_stopped())?
    }

    /// Executes a write on the requested priority lane.
    ///
    /// # Errors
    ///
    /// Returns a queue, connection, protocol, or device error.
    pub async fn write(
        &self,
        request: WriteRequest,
        priority: OperationPriority,
    ) -> CommResult<()> {
        let (response_tx, response_rx) = oneshot::channel();
        self.sender(priority)
            .try_send(SessionCommand::Write {
                request,
                response: response_tx,
            })
            .map_err(queue_error)?;
        response_rx.await.map_err(|_| worker_stopped())?
    }

    /// Creates a latest-value polling subscription.
    ///
    /// # Errors
    ///
    /// Returns an unavailable error after the session worker has stopped.
    pub fn watch(&self, spec: WatchSpec) -> CommResult<WatchSubscription> {
        let id = Uuid::new_v4();
        let initial = DeviceUpdate {
            key: spec.request.key.clone(),
            value: None,
            quality: ValueQuality::Uncertain,
            received_at: SystemTime::now(),
            error_code: None,
        };
        let (sender, receiver) = watch::channel(initial);
        self.watch_add_tx
            .send((id, spec, sender))
            .map_err(|_| worker_stopped())?;
        Ok(WatchSubscription::new(
            id,
            receiver,
            self.watch_cancel_tx.clone(),
        ))
    }

    /// Observes connection lifecycle changes.
    pub fn status(&self) -> watch::Receiver<DeviceStatus> {
        self.status_rx.clone()
    }

    /// Requests a graceful session shutdown.
    pub fn shutdown(&self) {
        self.shutdown.cancel();
    }

    fn sender(&self, priority: OperationPriority) -> &mpsc::Sender<SessionCommand> {
        match priority {
            OperationPriority::Control => &self.control_tx,
            OperationPriority::Normal => &self.normal_tx,
        }
    }
}

async fn run_session(
    client: Arc<dyn DeviceClient>,
    options: ManagedSessionOptions,
    mut control_rx: mpsc::Receiver<SessionCommand>,
    mut normal_rx: mpsc::Receiver<SessionCommand>,
    mut watch_add_rx: mpsc::UnboundedReceiver<(Uuid, WatchSpec, watch::Sender<DeviceUpdate>)>,
    mut watch_cancel_rx: mpsc::UnboundedReceiver<Uuid>,
    shutdown: CancellationToken,
) {
    let mut watches = HashMap::<Uuid, WatchEntry>::new();
    let mut ticker = tokio::time::interval(Duration::from_millis(10));
    ticker.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
    let mut control_burst = 0_usize;
    let mut reconnect_delay = options.reconnect_initial;

    loop {
        if shutdown.is_cancelled() {
            break;
        }

        let command = if control_burst < options.max_control_burst {
            control_rx.try_recv().ok().map(|value| (value, true))
        } else {
            normal_rx.try_recv().ok().map(|value| (value, false))
        };

        if let Some((command, was_control)) = command {
            execute_command(&*client, command, &mut reconnect_delay, &options).await;
            control_burst = if was_control { control_burst + 1 } else { 0 };
            continue;
        }

        tokio::select! {
            biased;
            () = shutdown.cancelled() => break,
            Some(id) = watch_cancel_rx.recv() => { watches.remove(&id); },
            Some((id, spec, sender)) = watch_add_rx.recv() => {
                watches.insert(id, WatchEntry { spec, next_due: Instant::now(), sender });
            },
            Some(command) = control_rx.recv(), if control_burst < options.max_control_burst => {
                execute_command(&*client, command, &mut reconnect_delay, &options).await;
                control_burst += 1;
            },
            Some(command) = normal_rx.recv() => {
                execute_command(&*client, command, &mut reconnect_delay, &options).await;
                control_burst = 0;
            },
            _ = ticker.tick() => {
                poll_due_watches(&*client, &mut watches, &mut reconnect_delay, &options).await;
                control_burst = 0;
            },
            else => break,
        }
    }

    let _ = client.disconnect().await;
    debug!("managed communication session stopped");
}

async fn execute_command(
    client: &dyn DeviceClient,
    command: SessionCommand,
    reconnect_delay: &mut Duration,
    options: &ManagedSessionOptions,
) {
    let connect_result = ensure_connected(client, reconnect_delay, options).await;
    match command {
        SessionCommand::Read { request, response } => {
            let result = match connect_result {
                Ok(()) => client.read(&request).await,
                Err(error) => Err(error),
            };
            disconnect_after_invalidating_error(client, &result).await;
            let _ = response.send(result);
        }
        SessionCommand::Write { request, response } => {
            let result = match connect_result {
                Ok(()) => client.write(&request).await,
                Err(error) => Err(error),
            };
            disconnect_after_invalidating_error(client, &result).await;
            let _ = response.send(result);
        }
    }
}

async fn poll_due_watches(
    client: &dyn DeviceClient,
    watches: &mut HashMap<Uuid, WatchEntry>,
    reconnect_delay: &mut Duration,
    options: &ManagedSessionOptions,
) {
    let now = Instant::now();
    let due = watches
        .iter()
        .filter_map(|(id, entry)| (entry.next_due <= now).then_some(*id))
        .collect::<Vec<_>>();

    if due.is_empty() {
        return;
    }

    let connected = ensure_connected(client, reconnect_delay, options).await;
    let mut connection_error = connected.as_ref().err().cloned();
    for id in due {
        let Some(entry) = watches.get_mut(&id) else {
            continue;
        };
        entry.next_due = now + entry.spec.interval;
        let result = match &connection_error {
            Some(error) => Err(error.clone()),
            None => client.read(&entry.spec.request).await,
        };
        if let Err(error) = &result
            && error.invalidates_connection()
        {
            let _ = client.disconnect().await;
            connection_error = Some(error.clone());
        }
        let update = match result {
            Ok(value) => DeviceUpdate {
                key: entry.spec.request.key.clone(),
                value: Some(value),
                quality: ValueQuality::Good,
                received_at: SystemTime::now(),
                error_code: None,
            },
            Err(error) => DeviceUpdate {
                key: entry.spec.request.key.clone(),
                value: None,
                quality: if error.invalidates_connection() {
                    ValueQuality::Disconnected
                } else {
                    ValueQuality::Bad
                },
                received_at: SystemTime::now(),
                error_code: Some(error.code),
            },
        };
        entry.sender.send_replace(update);
    }
}

async fn disconnect_after_invalidating_error<T>(client: &dyn DeviceClient, result: &CommResult<T>) {
    if result
        .as_ref()
        .is_err_and(CommError::invalidates_connection)
    {
        let _ = client.disconnect().await;
    }
}

async fn ensure_connected(
    client: &dyn DeviceClient,
    reconnect_delay: &mut Duration,
    options: &ManagedSessionOptions,
) -> CommResult<()> {
    if client.status().borrow().state == ConnectionState::Connected {
        *reconnect_delay = options.reconnect_initial;
        return Ok(());
    }

    match client.connect().await {
        Ok(()) => {
            *reconnect_delay = options.reconnect_initial;
            Ok(())
        }
        Err(error) => {
            warn!(code = %error.code, delay = ?*reconnect_delay, "device reconnect failed");
            tokio::time::sleep(*reconnect_delay).await;
            *reconnect_delay = (*reconnect_delay * 2).min(options.reconnect_max);
            Err(error)
        }
    }
}

#[allow(clippy::needless_pass_by_value)]
fn queue_error<T>(error: mpsc::error::TrySendError<T>) -> CommError {
    match error {
        mpsc::error::TrySendError::Full(_) => CommError::new(
            "COMM.BACKPRESSURE.QUEUE_FULL",
            CommErrorCategory::Backpressure,
            "The managed device queue is full",
            true,
        ),
        mpsc::error::TrySendError::Closed(_) => CommError::new(
            "COMM.UNAVAILABLE.SESSION_STOPPED",
            CommErrorCategory::Unavailable,
            "The managed device session has stopped",
            false,
        ),
    }
}

fn worker_stopped() -> CommError {
    CommError::new(
        "COMM.UNAVAILABLE.SESSION_STOPPED",
        CommErrorCategory::Unavailable,
        "The managed device session stopped before completing the operation",
        false,
    )
}
