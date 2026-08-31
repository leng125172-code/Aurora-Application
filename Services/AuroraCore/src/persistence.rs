//! Local durable outbox backed by redb, with optional PostgreSQL projection.

use anyhow::{Context, Result};
use redb::{Database, ReadableDatabase, ReadableTable, ReadableTableMetadata, TableDefinition};
use serde::{Deserialize, Serialize};
use std::path::Path;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio_postgres::{Client, NoTls};
use uuid::Uuid;

const OUTBOX: TableDefinition<&str, &str> = TableDefinition::new("station_event_outbox");

/// Durable station transition written before acknowledging a command.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StationEvent {
    /// Globally unique event id used for idempotent projection.
    pub event_id: Uuid,
    /// Logical station identity.
    pub station_id: String,
    /// Monotonic station sequence.
    pub sequence: u64,
    /// Previous state name.
    pub previous_state: String,
    /// New state name.
    pub new_state: String,
    /// Operator or service identity.
    pub operator_id: String,
    /// Human-readable transition reason.
    pub reason: String,
    /// Event timestamp.
    pub occurred_at: SystemTime,
}

/// Event journal that remains functional when PostgreSQL is unavailable.
pub struct EventJournal {
    local: Arc<Database>,
    postgres: Option<Client>,
}

impl EventJournal {
    /// Opens the redb outbox and optionally connects the PostgreSQL projection.
    pub async fn open(path: impl AsRef<Path>, postgres_url: Option<&str>) -> Result<Self> {
        if let Some(parent) = path.as_ref().parent() {
            std::fs::create_dir_all(parent).context("create Aurora data directory")?;
        }
        let local = Database::create(path).context("open redb station outbox")?;
        {
            let transaction = local.begin_write()?;
            transaction.open_table(OUTBOX)?;
            transaction.commit()?;
        }

        let postgres = if let Some(url) = postgres_url.filter(|value| !value.is_empty()) {
            match connect_postgres(url).await {
                Ok(client) => Some(client),
                Err(error) => {
                    tracing::warn!(%error, "PostgreSQL unavailable; continuing with durable local outbox");
                    None
                }
            }
        } else {
            None
        };

        Ok(Self {
            local: Arc::new(local),
            postgres,
        })
    }

    /// Persists locally first, then projects to PostgreSQL and clears the outbox entry.
    pub async fn append(&self, event: &StationEvent) -> Result<()> {
        let key = event.event_id.to_string();
        let payload = serde_json::to_string(event)?;
        {
            let transaction = self.local.begin_write()?;
            {
                let mut table = transaction.open_table(OUTBOX)?;
                table.insert(key.as_str(), payload.as_str())?;
            }
            transaction.commit()?;
        }

        if let Err(error) = self.project(event, &key).await {
            tracing::warn!(%error, event_id = %event.event_id, "event retained in local outbox");
        }
        Ok(())
    }

    /// Retries locally queued events after PostgreSQL becomes available.
    pub async fn flush_pending(&self) -> Result<usize> {
        if self.postgres.is_none() {
            return Ok(0);
        }
        let queued = {
            let transaction = self.local.begin_read()?;
            let table = transaction.open_table(OUTBOX)?;
            table
                .iter()?
                .map(|entry| {
                    let (key, value) = entry?;
                    Ok((key.value().to_owned(), value.value().to_owned()))
                })
                .collect::<Result<Vec<_>, redb::StorageError>>()?
        };
        let mut projected = 0;
        for (key, payload) in queued {
            let event = serde_json::from_str::<StationEvent>(&payload)?;
            match self.project(&event, &key).await {
                Ok(()) => projected += 1,
                Err(error) => {
                    tracing::warn!(%error, event_id = %event.event_id, "outbox flush paused");
                    break;
                }
            }
        }
        Ok(projected)
    }

    /// Number of events awaiting PostgreSQL projection.
    pub fn pending_count(&self) -> Result<u64> {
        let transaction = self.local.begin_read()?;
        let table = transaction.open_table(OUTBOX)?;
        Ok(table.len()?)
    }

    async fn project(&self, event: &StationEvent, key: &str) -> Result<()> {
        let Some(postgres) = &self.postgres else {
            return Ok(());
        };
        let sequence = i64::try_from(event.sequence).context("station sequence overflow")?;
        let occurred_ms = event.occurred_at.duration_since(UNIX_EPOCH)?.as_millis();
        let occurred_ms = i64::try_from(occurred_ms).context("event timestamp overflow")?;
        postgres
            .execute(
                "INSERT INTO aurora_station_event \
                 (event_id, station_id, sequence, previous_state, new_state, operator_id, reason, occurred_at) \
                 VALUES ($1::uuid, $2, $3, $4, $5, $6, $7, to_timestamp($8::double precision / 1000.0)) \
                 ON CONFLICT (event_id) DO NOTHING",
                &[&key, &event.station_id, &sequence, &event.previous_state,
                  &event.new_state, &event.operator_id, &event.reason, &occurred_ms],
            )
            .await?;
        let transaction = self.local.begin_write()?;
        {
            let mut table = transaction.open_table(OUTBOX)?;
            table.remove(key)?;
        }
        transaction.commit()?;
        Ok(())
    }
}

async fn connect_postgres(url: &str) -> Result<Client> {
    let (client, connection) = tokio_postgres::connect(url, NoTls)
        .await
        .context("connect station PostgreSQL projection")?;
    tokio::spawn(async move {
        if let Err(error) = connection.await {
            tracing::error!(%error, "PostgreSQL projection connection stopped");
        }
    });
    client
        .batch_execute(
            "CREATE TABLE IF NOT EXISTS aurora_station_event (\
             event_id uuid PRIMARY KEY, station_id text NOT NULL, sequence bigint NOT NULL, \
             previous_state text NOT NULL, new_state text NOT NULL, operator_id text NOT NULL, \
             reason text NOT NULL, occurred_at timestamptz NOT NULL, \
             UNIQUE(station_id, sequence))",
        )
        .await?;
    Ok(client)
}
