# PostgreSQL persistence MVP

## Local setup

Requires .NET 10 and Docker Compose. From the repository root (PowerShell):

```powershell
docker compose up -d --wait
$env:CT_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
dotnet run --project tools/ConquestusTerrarum.Migrations
```

Compose uses the official `postgres:17-alpine` image, a named data volume, and port
5432 bound to localhost. These credentials are **local development only**. Supply
production configuration outside source control. `docker compose stop` stops the
server while retaining data. There are no API endpoints or automatic startup workers.

The CLI calls `PostgresMigrator.MigrateAsync()`. SQL files in `db/migrations` are
embedded in Persistence. A transaction-level advisory lock serializes migrators;
`schema_migrations` tracks versions and SHA-256 checksums. Running migrations again
is safe, including concurrent starts. Applied files must not be edited: add a new
numbered file instead. Checksums normalize CRLF/LF so Windows and Linux agree.

## Snapshot contract

`world.CreateSnapshot()` returns immutable snapshot records. `WorldState.Restore()`
validates them and restores the original state without reconciliation or RNG use.
The Domain has no Npgsql dependency. `WorldSnapshotSerializer` uses System.Text.Json
with explicit DTOs, mandatory constructor fields, and unknown-field rejection.

The snapshot includes checkpoint, unsigned random seed, map locations/ownership/slot
counts, connections, alliances, armies and all movement/retreat state, individual
unit stats/HP, battle start/RNG state, and active slot identities/indices/pairing
times/starting HP/death boundaries. Destroyed armies retain identity/history. Derived
flags such as IsAlive and IsDestroyed follow saved HP; no new identity or random
value is generated during restore. Location, army, unit and battle collections use
stable ordering. Connection list order is preserved, including parallel edges.

JSON stores DateTimeOffset values with their offsets and seven fractional digits
when needed, preserving 100 ns simulation precision. Travel times are integer
TimeSpan ticks. Decimal HP/DPS and ulong RNG state round-trip without floating-point
conversion. PostgreSQL jsonb may reorder object keys or change numeric presentation;
logical values and timestamp strings are preserved. PostgreSQL metadata timestamps
have microsecond precision; simulation timestamps inside JSON retain tick precision.

`formatVersion = 1` versions this JSON contract independently of the match row's
`version`. Unknown formats and malformed/inconsistent state are rejected with
`InvalidWorldStateException`; they are never silently repaired. Future contract
changes need an explicit format migration. The snapshot API expects a world at a
consistent command/event checkpoint, not a partially applied event phase.

## Repository and time

Construct a reusable caller-owned `NpgsqlDataSource` and pass it to
`PostgresMatchRepository`. `CreateAsync(matchId, initialWorld)` stores the supplied
initial snapshot exactly, with version 0 and DB-generated creation/update time.
Its checkpoint must not be ahead of the database clock. Matchmaking is not included.

`ExecuteAsync(matchId, operation)` performs this sequence:

1. Begin a READ COMMITTED transaction.
2. Select the match row `FOR UPDATE`, waiting for any current lock holder.
3. Read `clock_timestamp()` **once, after acquiring the row lock**.
4. Deserialize, validate and restore the world; advance it to that captured time.
5. Call the domain operation with `(world, capturedTime)`.
6. Validate and serialize the resulting snapshot; update state, increment version,
   and set updated_at to the same captured time.
7. Commit. A callback failure, cancellation or validation failure rolls back the
   whole operation, including lazy simulation advancement.

The callback must use the supplied timestamp for commands, remain short, and avoid
external side effects or recursively executing on the same match. Moving the world
checkpoint past the captured time is rejected. Cancellation controls database work;
long-running callbacks should also observe the caller's cancellation token.

`LoadCurrentAsync` runs through that same execution path: even a read advances lazy
simulation, saves the checkpoint/state and increments the version. There is no
public stale read path. `MatchExecutionResult<T>` returns the callback result, saved
version and captured database time. `MatchNotFoundException` identifies missing rows;
Npgsql errors otherwise retain their original details.

Database server time is authoritative, never DateTimeOffset.UtcNow. We use
[clock_timestamp()](https://www.postgresql.org/docs/17/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT)
because PostgreSQL CURRENT_TIMESTAMP/now() report transaction start time. Sampling
before a row-lock wait could produce a time older than the preceding transaction's
saved checkpoint. Sampling once after the lock avoids that race. If the database
clock itself moves behind the checkpoint, `DatabaseTimeBehindWorldException` aborts
without changing state; retry when database time catches up. There is no clock clamp,
per-second simulation loop or scheduled polling.

One match is one locked aggregate: changes to arrivals, combat, RNG, casualties and
ownership commit together. Operations on the same match serialize; different matches
use different row locks. The version records every successful execution, including
reads. There is no automatic retry of command callbacks.

## Tests

```powershell
dotnet build
dotnet test
```

The normal suite runs domain snapshot and serializer tests without PostgreSQL;
integration tests are reported as skipped unless explicitly enabled:

```powershell
$env:CT_TEST_CONNECTION_STRING = $env:CT_CONNECTION_STRING
dotnet test tests/ConquestusTerrarum.Persistence.Tests --filter Category=Postgres
# Or run the entire solution with integration tests enabled:
dotnet test
Remove-Item Env:CT_TEST_CONNECTION_STRING
```

Use a disposable/local test database. The integration fixture creates and removes
only a uniquely named schema, requiring schema-creation privileges. A configured
but unavailable server fails tests rather than silently skipping them. No
Testcontainers dependency is required; Compose or any test PostgreSQL instance works.
The suite verifies exact initial storage, lazy reads, locks/concurrency, versions,
DB time, rollback, cancellation, corrupt state and repeatable migrations.

## Deliberate MVP limits

No simulation state is known to be missing from the round trip. A full JSON snapshot
is read, validated and rewritten per execution; this favors correctness over throughput.
Entities or queryable projections can be normalized later without changing domain
snapshot semantics. Snapshot upgrade tooling, retention/history, command idempotency,
production connection configuration and ambiguous commit recovery remain future work.
In particular, a network failure during COMMIT can leave the client unsure whether it
committed; the repository does not retry commands automatically. Database clock
regressions require retry after the clock catches up. Ownership after mutual
annihilation remains the existing **unresolved gameplay rule**: currently OwnerId is
unchanged when no army survives.
