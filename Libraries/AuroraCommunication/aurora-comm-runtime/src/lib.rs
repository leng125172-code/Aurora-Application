//! Managed device sessions for `AuroraCommunication`.

mod session;
mod watch;

pub use session::{ManagedSession, ManagedSessionOptions};
pub use watch::WatchSubscription;
