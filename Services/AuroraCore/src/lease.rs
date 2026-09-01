//! Exclusive remote-control lease with unconditional local-HMI preemption.

use std::sync::Arc;
use std::time::{Duration, SystemTime};
use tokio::sync::Mutex;
use uuid::Uuid;

/// A granted remote-control lease.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RemoteLease {
    /// Opaque capability presented with each remote command.
    pub token: String,
    /// Operator that owns the lease.
    pub operator_id: String,
    /// Absolute lease expiry.
    pub expires_at: SystemTime,
}

/// Coordinates one remote operator while allowing the local HMI to preempt it.
#[derive(Debug, Clone, Default)]
pub struct LeaseManager {
    active: Arc<Mutex<Option<RemoteLease>>>,
}

impl LeaseManager {
    /// Grants a lease for at most five minutes, replacing an expired lease only.
    ///
    /// # Errors
    ///
    /// Returns `CONTROL.LEASE.ALREADY_HELD` while another unexpired lease is
    /// active.
    pub async fn acquire(
        &self,
        operator_id: String,
        requested: Duration,
    ) -> Result<RemoteLease, &'static str> {
        let now = SystemTime::now();
        let mut active = self.active.lock().await;
        if active.as_ref().is_some_and(|lease| lease.expires_at > now) {
            return Err("CONTROL.LEASE.ALREADY_HELD");
        }
        let duration = requested.clamp(Duration::from_secs(10), Duration::from_secs(300));
        let lease = RemoteLease {
            token: Uuid::new_v4().to_string(),
            operator_id,
            expires_at: now + duration,
        };
        *active = Some(lease.clone());
        Ok(lease)
    }

    /// Validates the remote lease capability and its owner.
    pub async fn validate(&self, operator_id: &str, token: &str) -> bool {
        let now = SystemTime::now();
        self.active.lock().await.as_ref().is_some_and(|lease| {
            lease.expires_at > now && lease.operator_id == operator_id && lease.token == token
        })
    }

    /// Releases a lease when both owner and token match.
    pub async fn release(&self, operator_id: &str, token: &str) -> bool {
        let mut active = self.active.lock().await;
        let matches = active
            .as_ref()
            .is_some_and(|lease| lease.operator_id == operator_id && lease.token == token);
        if matches {
            *active = None;
        }
        matches
    }

    /// Revokes any remote lease. Every local-HMI command calls this first.
    pub async fn preempt_for_local(&self) {
        *self.active.lock().await = None;
    }
}
