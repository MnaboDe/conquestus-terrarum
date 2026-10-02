# HTTP API MVP

Requires .NET 10 and Docker Compose. From the repository root in PowerShell:

```powershell
docker compose up -d --wait
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
$env:CT_CONNECTION_STRING = $env:ConnectionStrings__Postgres
dotnet run --project tools/ConquestusTerrarum.Migrations
dotnet run --project src/ConquestusTerrarum.Api --launch-profile http
```

These credentials are local development only. This launch profile selects Development and HTTP port 5108. Migrations run
explicitly through the CLI; the API never runs migrations or background simulation. DI owns one reusable singleton
`NpgsqlDataSource`
configured by `ConnectionStrings:Postgres`, and a singleton `IMatchRepository` implemented by `PostgresMatchRepository`.
The environment variable overrides development appsettings.

## Try the routes

In a second PowerShell window:

```powershell
$base = 'http://localhost:5108'
Invoke-RestMethod "$base/openapi/v1.json"
$state = Invoke-RestMethod "$base/api/v1/dev/matches" -Method Post
$matchId = $state.matchId
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId"
```

Creation returns 201, a Location header, and the full MatchStateResponse including matchId and version 0. Creation and
OpenAPI exist only in Development (404 elsewhere). The demo contains four locations, five armies, two allied players and
a hostile player. Demo unit stats are illustrative, not a production balance specification.

Move armies from both camps to Crossroads, then retreat after combat starts. Run this whole block together so the battle
is still active when retreat is requested:

```powershell
$west = ($state.locations | Where-Object name -eq 'West camp').id
$east = ($state.locations | Where-Object name -eq 'East camp').id
$crossroads = ($state.locations | Where-Object name -eq 'Crossroads').id
$westArmy = ($state.armies | Where-Object locationId -eq $west | Select-Object -First 1).id
$eastArmy = ($state.armies | Where-Object locationId -eq $east | Select-Object -First 1).id
$body = @{ destinationLocationId = $crossroads } | ConvertTo-Json
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$westArmy/move" -Method Post -ContentType 'application/json' -Body $body
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$eastArmy/move" -Method Post -ContentType 'application/json' -Body $body
Start-Sleep -Seconds 6
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId"
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$westArmy/retreat" -Method Post
$state.armies | Where-Object id -eq $westArmy
```

Retreat follows existing domain rules, including loss of wounded units. Illegal actions return 409. Retreat and
development creation accept no body. Move accepts only
`destinationLocationId`; missing, malformed, empty or extra fields are rejected. These routes accept no query
parameters, including command timestamps.

Stop the API with Ctrl+C, then `docker compose stop` to retain database data.

## Contract and transactions

GET `/api/v1/matches/{matchId}`, POST `/api/v1/matches/{matchId}/armies/{armyId}/move`
and POST `/api/v1/matches/{matchId}/armies/{armyId}/retreat` return the same explicit client contract. Each executes one
repository transaction: lock the match row, capture PostgreSQL `clock_timestamp()` once after locking, restore and
advance lazy simulation, apply the command if present, map the state, persist and increment version, then commit. A
successful command response contains that transaction's state and persisted version; no second GET is needed. GET itself
advances, saves and increments version. Responses use `Cache-Control: no-store`. Failed commands roll back lazy
advancement as well as command changes. Request cancellation propagates into persistence without becoming 500.

**Client timestamps are never authoritative.** Commands use exactly the database time supplied by the repository.
Development creation uses the factory overload, so its initial checkpoint also comes from PostgreSQL. serverTime and
checkpointTime describe the captured operation time, not response delivery. Movement timestamps support local animation;
the server remains authoritative.

`MatchStateResponse` contains explicit `LocationResponse`, `ConnectionResponse`,
`ArmyResponse`, `MovementResponse`, `UnitResponse`, `AllianceResponse`, `BattleResponse`
and `CombatSlotResponse` records. Input uses `MoveArmyRequest`. Collections are sorted by identity (slots by index,
connections by endpoints and travel ticks). Movement kind is `normal` or `retreat`. Dates are offset-preserving JSON
timestamps; travel times are integer TimeSpan ticks (100 ns). HP/DPS are decimals. Nullable fields remain explicit.
World random seed, battle RNG state and persistence snapshots are not exposed. The complete Development schema is
available at `/openapi/v1.json`.

Handled errors use application/problem+json with status, title, detail, type, code and traceId. Missing match is 404;
invalid input is 400 (unsupported content type can be 415); illegal gameplay is 409; database clock behind the
checkpoint or transient database failure is 503; corrupt state and unexpected failures are 500 with sanitized details.
Exception stack traces stay out of responses.

## Tests and later work

```powershell
dotnet build
dotnet test
# Optional persistence and HTTP end-to-end tests:
$env:CT_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
dotnet test --filter Category=Postgres
Remove-Item Env:CT_TEST_CONNECTION_STRING
```

Without the test connection string, PostgreSQL tests are skipped. Enabled tests create and remove unique schemas; use a
disposable local database with schema creation rights. Normal API tests replace the repository and exercise real
routing, validation, mapping and domain commands without PostgreSQL.

This local MVP has no authentication, authorization, ownership enforcement or player-specific visibility: callers see
the full match and can command any army. Production access control, visibility, command idempotency/retry policy,
transport deployment and Unity remain later work. There is no push or polling service. A connection failure during
commit can leave the caller uncertain whether a command committed; commands are not retried automatically. Gameplay is
unchanged, including the unresolved ownership rule after mutual annihilation.
