//! RAII watch subscription.

use aurora_comm_core::DeviceUpdate;
use tokio::sync::{mpsc, watch};
use uuid::Uuid;

/// A latest-value watch subscription. Dropping it cancels future polling.
pub struct WatchSubscription {
    id: Uuid,
    receiver: watch::Receiver<DeviceUpdate>,
    cancel_tx: mpsc::UnboundedSender<Uuid>,
}

impl WatchSubscription {
    pub(crate) fn new(
        id: Uuid,
        receiver: watch::Receiver<DeviceUpdate>,
        cancel_tx: mpsc::UnboundedSender<Uuid>,
    ) -> Self {
        Self {
            id,
            receiver,
            cancel_tx,
        }
    }

    /// Returns a receiver that always retains the latest update.
    pub fn receiver(&self) -> watch::Receiver<DeviceUpdate> {
        self.receiver.clone()
    }

    /// Returns the latest update without waiting.
    pub fn latest(&self) -> DeviceUpdate {
        self.receiver.borrow().clone()
    }
}

impl Drop for WatchSubscription {
    fn drop(&mut self) {
        let _ = self.cancel_tx.send(self.id);
    }
}
