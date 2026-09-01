//! Safety-state and control-lease acceptance tests.

use aurora_core_service::actor::{StationCommandOrigin, StationHandle, StationRunState};
use aurora_core_service::lease::LeaseManager;
use aurora_core_service::persistence::EventJournal;
use std::sync::Arc;
use std::time::Duration;

#[tokio::test]
async fn restart_requires_local_acknowledgement_before_start() {
    let directory = tempfile::tempdir().expect("temporary directory");
    let journal = Arc::new(
        EventJournal::open(directory.path().join("outbox.redb"), None)
            .await
            .expect("journal should open"),
    );
    let station =
        StationHandle::spawn("main", Arc::clone(&journal)).expect("startup event should persist");

    let initial = station.get().await.expect("actor should respond");
    assert_eq!(initial.state, StationRunState::SafeStop);
    assert!(initial.requires_manual_acknowledgement);
    assert!(
        station
            .set_state(
                StationRunState::Running,
                StationCommandOrigin::LocalHmi,
                "local-operator".to_owned(),
                "attempt before acknowledgement".to_owned(),
            )
            .await
            .is_err()
    );

    let idle = station
        .set_state(
            StationRunState::Idle,
            StationCommandOrigin::LocalHmi,
            "local-operator".to_owned(),
            "physical inspection complete".to_owned(),
        )
        .await
        .expect("safe stop should acknowledge");
    assert!(!idle.requires_manual_acknowledgement);
    let running = station
        .set_state(
            StationRunState::Running,
            StationCommandOrigin::LocalHmi,
            "local-operator".to_owned(),
            "production start".to_owned(),
        )
        .await
        .expect("idle station should start");
    assert_eq!(running.state, StationRunState::Running);
    assert_eq!(journal.pending_count().expect("outbox count"), 3);
}

#[tokio::test]
async fn restart_enters_safe_stop_with_a_monotonic_audit_sequence() {
    let directory = tempfile::tempdir().expect("temporary directory");
    let journal = Arc::new(
        EventJournal::open(directory.path().join("outbox.redb"), None)
            .await
            .expect("journal should open"),
    );
    let first = StationHandle::spawn("main", Arc::clone(&journal))
        .expect("first startup event should persist");
    assert_eq!(first.cached_snapshot().sequence, 1);
    first
        .set_state(
            StationRunState::Idle,
            StationCommandOrigin::LocalHmi,
            "local-operator".to_owned(),
            "physical inspection complete".to_owned(),
        )
        .await
        .expect("safe stop should acknowledge");
    drop(first);
    tokio::task::yield_now().await;

    let restarted =
        StationHandle::spawn("main", Arc::clone(&journal)).expect("restart event should persist");
    let snapshot = restarted.cached_snapshot();
    assert_eq!(snapshot.state, StationRunState::SafeStop);
    assert!(snapshot.requires_manual_acknowledgement);
    assert_eq!(snapshot.sequence, 3);
    assert_eq!(journal.last_sequence("main").expect("last sequence"), 3);
    assert_eq!(journal.pending_count().expect("outbox count"), 3);
}

#[tokio::test]
async fn local_hmi_preempts_remote_lease() {
    let leases = LeaseManager::default();
    let lease = leases
        .acquire("remote-user".to_owned(), Duration::from_secs(60))
        .await
        .expect("lease should be granted");
    assert!(leases.validate("remote-user", &lease.token).await);

    leases.preempt_for_local().await;

    assert!(!leases.validate("remote-user", &lease.token).await);
}
