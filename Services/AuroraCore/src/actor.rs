//! One serialized actor owns each station's mutable run state.

use crate::persistence::{EventJournal, StationEvent};
use anyhow::{Result, anyhow};
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use std::time::SystemTime;
use tokio::sync::{mpsc, oneshot, watch};
use uuid::Uuid;

/// State of a station workflow, independent from any UI framework.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum StationRunState {
    /// Outputs are safe and a local acknowledgement is required.
    SafeStop,
    /// Ready but not executing.
    Idle,
    /// Workflow is executing.
    Running,
    /// A device or workflow fault requires operator action.
    Faulted,
}

/// Trusted source that initiated a station transition.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum StationCommandOrigin {
    /// Core lifecycle or an internal safety action.
    CoreService,
    /// Operator physically present at the machine HMI.
    LocalHmi,
    /// Authenticated remote operator holding the control lease.
    RemoteWeb,
}

/// Immutable station state published to API clients.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct StationSnapshot {
    /// Station identity.
    pub station_id: String,
    /// Current run state.
    pub state: StationRunState,
    /// Monotonic transition sequence.
    pub sequence: u64,
    /// Whether a local operator must acknowledge the safe stop.
    pub requires_manual_acknowledgement: bool,
    /// Latest transition reason.
    pub last_reason: String,
    /// Latest transition timestamp.
    pub changed_at: SystemTime,
}

enum StationCommand {
    Transition {
        target: StationRunState,
        origin: StationCommandOrigin,
        operator_id: String,
        reason: String,
        response: oneshot::Sender<Result<StationSnapshot>>,
    },
    Get {
        response: oneshot::Sender<StationSnapshot>,
    },
}

/// Cloneable command handle for a station actor.
#[derive(Clone)]
pub struct StationHandle {
    sender: mpsc::Sender<StationCommand>,
    snapshot: watch::Receiver<StationSnapshot>,
}

impl StationHandle {
    /// Starts a station in safe-stop state and durably records the service startup.
    ///
    /// # Errors
    ///
    /// Returns an error when the startup safety event cannot be persisted or
    /// the station sequence is exhausted.
    pub fn spawn(station_id: impl Into<String>, journal: Arc<EventJournal>) -> Result<Self> {
        let station_id = station_id.into();
        let sequence = journal
            .last_sequence(&station_id)?
            .checked_add(1)
            .ok_or_else(|| anyhow!("station sequence exhausted"))?;
        let changed_at = SystemTime::now();
        journal.append(&StationEvent {
            event_id: Uuid::new_v4(),
            station_id: station_id.clone(),
            sequence,
            previous_state: "ServiceOffline".to_owned(),
            new_state: format!("{:?}", StationRunState::SafeStop),
            origin: format!("{:?}", StationCommandOrigin::CoreService),
            operator_id: "aurora-core-service".to_owned(),
            reason: "CORE_SERVICE_STARTED".to_owned(),
            occurred_at: changed_at,
        })?;
        let initial = StationSnapshot {
            station_id,
            state: StationRunState::SafeStop,
            sequence,
            requires_manual_acknowledgement: true,
            last_reason: "CORE_SERVICE_STARTED".to_owned(),
            changed_at,
        };
        let (snapshot_tx, snapshot) = watch::channel(initial.clone());
        let (sender, mut receiver) = mpsc::channel(128);
        tokio::spawn(async move {
            let mut current = initial;
            while let Some(command) = receiver.recv().await {
                match command {
                    StationCommand::Get { response } => {
                        let _ = response.send(current.clone());
                    }
                    StationCommand::Transition {
                        target,
                        origin,
                        operator_id,
                        reason,
                        response,
                    } => {
                        let result = transition(&current, target);
                        match result {
                            Ok(()) => {
                                let previous = current.state;
                                let event = StationEvent {
                                    event_id: Uuid::new_v4(),
                                    station_id: current.station_id.clone(),
                                    sequence: current.sequence + 1,
                                    previous_state: format!("{previous:?}"),
                                    new_state: format!("{target:?}"),
                                    origin: format!("{origin:?}"),
                                    operator_id,
                                    reason: reason.clone(),
                                    occurred_at: SystemTime::now(),
                                };
                                if let Err(error) = journal.append(&event) {
                                    tracing::error!(%error, "station transition journal failed");
                                    let _ = response.send(Err(error));
                                    continue;
                                }
                                current.state = target;
                                current.sequence += 1;
                                current.requires_manual_acknowledgement =
                                    target == StationRunState::SafeStop;
                                current.last_reason = reason;
                                current.changed_at = event.occurred_at;
                                snapshot_tx.send_replace(current.clone());
                                let _ = response.send(Ok(current.clone()));
                            }
                            Err(error) => {
                                let _ = response.send(Err(error));
                            }
                        }
                    }
                }
            }
        });
        Ok(Self { sender, snapshot })
    }

    /// Returns the latest in-memory snapshot without a database round trip.
    pub fn cached_snapshot(&self) -> StationSnapshot {
        self.snapshot.borrow().clone()
    }

    /// Reads through the actor mailbox, useful as a liveness check.
    ///
    /// # Errors
    ///
    /// Returns an error when the actor mailbox or response channel is closed.
    pub async fn get(&self) -> Result<StationSnapshot> {
        let (response, receiver) = oneshot::channel();
        self.sender.send(StationCommand::Get { response }).await?;
        Ok(receiver.await?)
    }

    /// Requests a validated state transition.
    ///
    /// # Errors
    ///
    /// Returns an error when the mailbox is unavailable, persistence fails, or
    /// the requested transition violates the station state machine.
    pub async fn set_state(
        &self,
        target: StationRunState,
        origin: StationCommandOrigin,
        operator_id: String,
        reason: String,
    ) -> Result<StationSnapshot> {
        let (response, receiver) = oneshot::channel();
        self.sender
            .send(StationCommand::Transition {
                target,
                origin,
                operator_id,
                reason,
                response,
            })
            .await?;
        receiver.await?
    }
}

fn transition(current: &StationSnapshot, target: StationRunState) -> Result<()> {
    let allowed = matches!(
        (current.state, target),
        (
            StationRunState::SafeStop | StationRunState::Running,
            StationRunState::Idle
        ) | (StationRunState::Idle, StationRunState::Running)
            | (_, StationRunState::SafeStop | StationRunState::Faulted)
    );
    if allowed {
        Ok(())
    } else {
        Err(anyhow!(
            "STATION.TRANSITION.INVALID: {:?} -> {:?}",
            current.state,
            target
        ))
    }
}
