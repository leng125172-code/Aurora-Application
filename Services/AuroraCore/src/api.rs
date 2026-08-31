//! Local-only gRPC boundary consumed by the .NET HMI host.

use crate::actor::{StationHandle, StationRunState, StationSnapshot};
use crate::contracts::station_control_server::StationControl;
use crate::contracts::{
    AcquireRemoteLeaseRequest, CommandReply, ControlOrigin, GetStationRequest,
    ReleaseRemoteLeaseRequest, RemoteLeaseReply, StationCommandRequest,
};
use crate::lease::LeaseManager;
use std::time::{Duration, SystemTime, UNIX_EPOCH};
use tonic::{Request, Response, Status};

/// gRPC façade over station actors and remote-control leases.
pub struct StationControlApi {
    station: StationHandle,
    lease: LeaseManager,
}

impl StationControlApi {
    /// Creates an API for one station. A registry can replace this once multiple stations exist.
    pub const fn new(station: StationHandle, lease: LeaseManager) -> Self {
        Self { station, lease }
    }

    fn require_station(&self, station_id: &str) -> Result<(), Status> {
        if self.station.cached_snapshot().station_id == station_id {
            Ok(())
        } else {
            Err(Status::not_found("STATION.NOT_FOUND"))
        }
    }

    async fn authorize(&self, request: &StationCommandRequest) -> Result<(), Status> {
        self.require_station(&request.station_id)?;
        match ControlOrigin::try_from(request.origin).unwrap_or(ControlOrigin::Unspecified) {
            ControlOrigin::LocalHmi => {
                self.lease.preempt_for_local().await;
                Ok(())
            }
            ControlOrigin::RemoteWeb
                if self
                    .lease
                    .validate(&request.operator_id, &request.lease_token)
                    .await =>
            {
                Ok(())
            }
            ControlOrigin::RemoteWeb => Err(Status::permission_denied("CONTROL.LEASE.INVALID")),
            ControlOrigin::Unspecified => Err(Status::invalid_argument("CONTROL.ORIGIN.REQUIRED")),
        }
    }

    async fn command(
        &self,
        request: StationCommandRequest,
        target: StationRunState,
    ) -> Result<Response<CommandReply>, Status> {
        self.authorize(&request).await?;
        let reason = if request.reason.trim().is_empty() {
            format!("{target:?}")
        } else {
            request.reason
        };
        let reply = match self
            .station
            .set_state(target, request.operator_id, reason)
            .await
        {
            Ok(snapshot) => accepted(snapshot),
            Err(error) => rejected("STATION.COMMAND.REJECTED", error.to_string()),
        };
        Ok(Response::new(reply))
    }
}

#[tonic::async_trait]
impl StationControl for StationControlApi {
    async fn get_station(
        &self,
        request: Request<GetStationRequest>,
    ) -> Result<Response<crate::contracts::StationSnapshot>, Status> {
        self.require_station(&request.get_ref().station_id)?;
        let snapshot = self
            .station
            .get()
            .await
            .map_err(|error| Status::unavailable(error.to_string()))?;
        Ok(Response::new(snapshot.into()))
    }

    async fn start(
        &self,
        request: Request<StationCommandRequest>,
    ) -> Result<Response<CommandReply>, Status> {
        self.command(request.into_inner(), StationRunState::Running)
            .await
    }

    async fn stop(
        &self,
        request: Request<StationCommandRequest>,
    ) -> Result<Response<CommandReply>, Status> {
        self.command(request.into_inner(), StationRunState::Idle)
            .await
    }

    async fn emergency_stop(
        &self,
        request: Request<StationCommandRequest>,
    ) -> Result<Response<CommandReply>, Status> {
        self.command(request.into_inner(), StationRunState::SafeStop)
            .await
    }

    async fn acknowledge_safe_stop(
        &self,
        request: Request<StationCommandRequest>,
    ) -> Result<Response<CommandReply>, Status> {
        let request = request.into_inner();
        if ControlOrigin::try_from(request.origin).unwrap_or(ControlOrigin::Unspecified)
            != ControlOrigin::LocalHmi
        {
            return Err(Status::permission_denied(
                "STATION.SAFE_STOP.LOCAL_ACK_REQUIRED",
            ));
        }
        self.command(request, StationRunState::Idle).await
    }

    async fn acquire_remote_lease(
        &self,
        request: Request<AcquireRemoteLeaseRequest>,
    ) -> Result<Response<RemoteLeaseReply>, Status> {
        let request = request.into_inner();
        self.require_station(&request.station_id)?;
        if request.operator_id.trim().is_empty() {
            return Err(Status::invalid_argument("CONTROL.OPERATOR.REQUIRED"));
        }
        let requested = Duration::from_secs(u64::from(request.requested_seconds));
        let reply = match self.lease.acquire(request.operator_id, requested).await {
            Ok(lease) => RemoteLeaseReply {
                accepted: true,
                lease_token: lease.token,
                expires_at_unix_ms: unix_ms(lease.expires_at),
                error_code: String::new(),
                message: "Remote control lease granted".to_owned(),
            },
            Err(code) => RemoteLeaseReply {
                accepted: false,
                lease_token: String::new(),
                expires_at_unix_ms: 0,
                error_code: code.to_owned(),
                message: "Another remote operator holds the control lease".to_owned(),
            },
        };
        Ok(Response::new(reply))
    }

    async fn release_remote_lease(
        &self,
        request: Request<ReleaseRemoteLeaseRequest>,
    ) -> Result<Response<CommandReply>, Status> {
        let request = request.into_inner();
        self.require_station(&request.station_id)?;
        let released = self
            .lease
            .release(&request.operator_id, &request.lease_token)
            .await;
        Ok(Response::new(if released {
            CommandReply {
                accepted: true,
                error_code: String::new(),
                message: "Remote control lease released".to_owned(),
                snapshot: Some(self.station.cached_snapshot().into()),
            }
        } else {
            rejected(
                "CONTROL.LEASE.INVALID",
                "Remote control lease did not match".to_owned(),
            )
        }))
    }
}

fn accepted(snapshot: StationSnapshot) -> CommandReply {
    CommandReply {
        accepted: true,
        error_code: String::new(),
        message: "Command accepted".to_owned(),
        snapshot: Some(snapshot.into()),
    }
}

fn rejected(code: &str, message: String) -> CommandReply {
    CommandReply {
        accepted: false,
        error_code: code.to_owned(),
        message,
        snapshot: None,
    }
}

fn unix_ms(value: SystemTime) -> i64 {
    value
        .duration_since(UNIX_EPOCH)
        .ok()
        .and_then(|duration| i64::try_from(duration.as_millis()).ok())
        .unwrap_or_default()
}

impl From<StationSnapshot> for crate::contracts::StationSnapshot {
    fn from(value: StationSnapshot) -> Self {
        let state = match value.state {
            StationRunState::SafeStop => crate::contracts::StationRunState::SafeStop,
            StationRunState::Idle => crate::contracts::StationRunState::Idle,
            StationRunState::Running => crate::contracts::StationRunState::Running,
            StationRunState::Faulted => crate::contracts::StationRunState::Faulted,
        };
        Self {
            station_id: value.station_id,
            state: state.into(),
            sequence: value.sequence,
            requires_manual_acknowledgement: value.requires_manual_acknowledgement,
            last_reason: value.last_reason,
            changed_at_unix_ms: unix_ms(value.changed_at),
        }
    }
}
