//! Local durable outbox backed by redb, with optional PostgreSQL projection.

use anyhow::{Context, Result, bail};
use redb::{Database, ReadableDatabase, ReadableTable, ReadableTableMetadata, TableDefinition};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::Mutex;
use tokio_postgres::{Client, NoTls};
use uuid::Uuid;

const OUTBOX: TableDefinition<&str, &str> = TableDefinition::new("station_event_outbox");
const STATION_SEQUENCES: TableDefinition<&str, u64> =
    TableDefinition::new("station_event_sequences");

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
    /// Trusted command boundary that initiated the transition.
    #[serde(default = "unknown_origin")]
    pub origin: String,
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
    postgres_url: Option<String>,
    postgres: Mutex<Option<Client>>,
}

impl EventJournal {
    /// Opens the redb outbox and optionally connects the PostgreSQL projection.
    ///
    /// # Errors
    ///
    /// Returns an error when the local directory or redb outbox cannot be
    /// created. PostgreSQL connection failures are non-fatal and use the local
    /// outbox instead.
    pub async fn open(path: impl AsRef<Path>, postgres_url: Option<&str>) -> Result<Self> {
        if let Some(parent) = path.as_ref().parent() {
            std::fs::create_dir_all(parent).context("create Aurora data directory")?;
        }
        let local = Database::create(path).context("open redb station outbox")?;
        {
            let transaction = local.begin_write()?;
            transaction.open_table(OUTBOX)?;
            transaction.open_table(STATION_SEQUENCES)?;
            transaction.commit()?;
        }
        backfill_sequences_from_outbox(&local)?;

        let postgres_url = postgres_url
            .filter(|value| !value.is_empty())
            .map(str::to_owned);
        let postgres = if let Some(url) = postgres_url.as_deref() {
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

        if let Some(client) = &postgres {
            synchronize_sequences_from_postgres(&local, client).await?;
        }

        Ok(Self {
            local: Arc::new(local),
            postgres_url,
            postgres: Mutex::new(postgres),
        })
    }

    /// Persists to the local outbox without waiting for PostgreSQL projection.
    ///
    /// # Errors
    ///
    /// Returns an error when serialization or durable local persistence fails.
    /// PostgreSQL projection is performed by [`Self::flush_pending`].
    pub fn append(&self, event: &StationEvent) -> Result<()> {
        let key = event.event_id.to_string();
        let payload = serde_json::to_string(event)?;
        {
            let transaction = self.local.begin_write()?;
            {
                let mut sequences = transaction.open_table(STATION_SEQUENCES)?;
                let current = sequences
                    .get(event.station_id.as_str())?
                    .map(|value| value.value())
                    .unwrap_or_default();
                let expected = current
                    .checked_add(1)
                    .context("station sequence exhausted")?;
                if event.sequence != expected {
                    bail!(
                        "station {} sequence must be {}, received {}",
                        event.station_id,
                        expected,
                        event.sequence
                    );
                }
                sequences.insert(event.station_id.as_str(), event.sequence)?;
            }
            {
                let mut table = transaction.open_table(OUTBOX)?;
                table.insert(key.as_str(), payload.as_str())?;
            }
            transaction.commit()?;
        }

        Ok(())
    }

    /// Retries locally queued events after PostgreSQL becomes available.
    ///
    /// # Errors
    ///
    /// Returns an error when the local outbox cannot be read or an event cannot
    /// be deserialized.
    pub async fn flush_pending(&self) -> Result<usize> {
        let Some(postgres_url) = self.postgres_url.as_deref() else {
            return Ok(0);
        };
        let mut postgres = self.postgres.lock().await;
        if postgres.is_none() {
            match connect_postgres(postgres_url).await {
                Ok(client) => {
                    synchronize_sequences_from_postgres(&self.local, &client).await?;
                    *postgres = Some(client);
                    tracing::info!("PostgreSQL projection reconnected");
                }
                Err(error) => {
                    tracing::warn!(%error, "PostgreSQL projection remains unavailable");
                    return Ok(0);
                }
            }
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
            let Some(client) = postgres.as_ref() else {
                break;
            };
            let result = self.project(client, &event, &key).await;
            match result {
                Ok(()) => projected += 1,
                Err(error) => {
                    tracing::warn!(%error, event_id = %event.event_id, "outbox flush paused");
                    *postgres = None;
                    break;
                }
            }
        }
        Ok(projected)
    }

    /// Number of events awaiting PostgreSQL projection.
    ///
    /// # Errors
    ///
    /// Returns an error when the local database or outbox table cannot be read.
    pub fn pending_count(&self) -> Result<u64> {
        let transaction = self.local.begin_read()?;
        let table = transaction.open_table(OUTBOX)?;
        Ok(table.len()?)
    }

    /// Last durably assigned event sequence for a station.
    ///
    /// # Errors
    ///
    /// Returns an error when the local sequence table cannot be read.
    pub fn last_sequence(&self, station_id: &str) -> Result<u64> {
        let transaction = self.local.begin_read()?;
        let table = transaction.open_table(STATION_SEQUENCES)?;
        Ok(table
            .get(station_id)?
            .map(|value| value.value())
            .unwrap_or_default())
    }

    async fn project(&self, postgres: &Client, event: &StationEvent, key: &str) -> Result<()> {
        let sequence = i64::try_from(event.sequence).context("station sequence overflow")?;
        let occurred_ms = event.occurred_at.duration_since(UNIX_EPOCH)?.as_millis();
        let occurred_ms = i64::try_from(occurred_ms).context("event timestamp overflow")?;
        postgres
            .execute(
                "INSERT INTO aurora_station_event \
                 (event_id, station_id, sequence, previous_state, new_state, origin, operator_id, reason, occurred_at) \
                 VALUES ($1::uuid, $2, $3, $4, $5, $6, $7, $8, to_timestamp($9::double precision / 1000.0)) \
                 ON CONFLICT (event_id) DO NOTHING",
                &[&key, &event.station_id, &sequence, &event.previous_state,
                  &event.new_state, &event.origin, &event.operator_id, &event.reason, &occurred_ms],
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

fn unknown_origin() -> String {
    "Unknown".to_owned()
}

fn backfill_sequences_from_outbox(local: &Database) -> Result<()> {
    let queued = {
        let transaction = local.begin_read()?;
        let table = transaction.open_table(OUTBOX)?;
        table
            .iter()?
            .map(|entry| {
                let (_, value) = entry?;
                serde_json::from_str::<StationEvent>(value.value()).map_err(anyhow::Error::from)
            })
            .collect::<Result<Vec<_>>>()?
    };
    let mut maxima = HashMap::<String, u64>::new();
    for event in queued {
        maxima
            .entry(event.station_id)
            .and_modify(|sequence| *sequence = (*sequence).max(event.sequence))
            .or_insert(event.sequence);
    }
    merge_sequences(local, maxima)
}

async fn synchronize_sequences_from_postgres(local: &Database, client: &Client) -> Result<()> {
    let rows = client
        .query(
            "SELECT station_id, MAX(sequence) FROM aurora_station_event GROUP BY station_id",
            &[],
        )
        .await?;
    let mut maxima = HashMap::new();
    for row in rows {
        let station_id = row.get::<_, String>(0);
        let sequence = row.get::<_, i64>(1);
        maxima.insert(
            station_id,
            u64::try_from(sequence).context("negative PostgreSQL station sequence")?,
        );
    }
    merge_sequences(local, maxima)
}

fn merge_sequences(local: &Database, maxima: HashMap<String, u64>) -> Result<()> {
    if maxima.is_empty() {
        return Ok(());
    }
    let transaction = local.begin_write()?;
    {
        let mut table = transaction.open_table(STATION_SEQUENCES)?;
        for (station_id, candidate) in maxima {
            let current = table
                .get(station_id.as_str())?
                .map(|value| value.value())
                .unwrap_or_default();
            if candidate > current {
                table.insert(station_id.as_str(), candidate)?;
            }
        }
    }
    transaction.commit()?;
    Ok(())
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
             previous_state text NOT NULL, new_state text NOT NULL, origin text NOT NULL, operator_id text NOT NULL, \
             reason text NOT NULL, occurred_at timestamptz NOT NULL, \
             UNIQUE(station_id, sequence)); \
             ALTER TABLE aurora_station_event \
             ADD COLUMN IF NOT EXISTS origin text NOT NULL DEFAULT 'Unknown'",
        )
        .await?;
    Ok(client)
}
