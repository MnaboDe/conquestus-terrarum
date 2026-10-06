# Two-player HTTP API MVP

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
configured by `ConnectionStrings:Postgres`, `IMatchRepository` implemented by `PostgresMatchRepository`, and
`IMatchSessionRepository` implemented by `PostgresMatchSessionRepository`.
The environment variable overrides development appsettings.

## Try the routes

In a second PowerShell window:

```powershell
$base = 'http://localhost:5108'
Invoke-RestMethod "$base/openapi/v1.json"
$first = Invoke-RestMethod "$base/api/v1/matches" -Method Post
$matchId = $first.matchId
$firstHeaders = @{ Authorization = "Bearer $($first.playerToken)" }
# Share only the join code with the second player.
$joinBody = @{ joinCode = $first.joinCode } | ConvertTo-Json
$second = Invoke-RestMethod "$base/api/v1/matches/join" -Method Post -ContentType 'application/json' -Body $joinBody
$secondHeaders = @{ Authorization = "Bearer $($second.playerToken)" }
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId" -Headers $firstHeaders
```

Production creation returns 201, a Location header, and `{ matchId, playerId, playerToken, joinCode }`.
It accepts no body or query parameters and creates a fixed four-location map with two armies per player.
The second identity is reserved in the initial world before joining. Join returns 200 and
`{ matchId, playerId, playerToken }`; it accepts only `joinCode`, normalized by trimming outer whitespace and uppercasing.
Codes contain six unambiguous uppercase letters/digits. A well-formed unknown code returns 404, malformed input 400,
and a claimed second slot 409. Concurrent claims cannot both succeed. Unit stats are MVP values.

The bearer token is a secret: keep it private and retain it locally before using protected routes. The raw token is
returned only by create/join; there is no recovery endpoint, permanent account, password, or token refresh in this MVP.
Do not log credentials or Authorization headers. Join codes grant the open second slot and are separate from credentials.

`POST /api/v1/dev/matches` and OpenAPI remain Development-only (404 elsewhere). Development creation retains its original
full-snapshot response and three-owner demo world, without issuing session credentials. Use production creation for
authenticated multiplayer; existing tests can seed sidecar credentials for development/import worlds. All production
match routes require authentication, including in Development. Unity's Create/Join flow now uses these production routes;
see the [two-client test guide](../../client/MULTIPLAYER_TEST.md).

Move armies from both camps to Crossroads, then retreat after combat starts. Run this whole block together so the battle
is still active when retreat is requested:

```powershell
$west = ($state.locations | Where-Object name -eq 'West camp').id
$east = ($state.locations | Where-Object name -eq 'East camp').id
$crossroads = ($state.locations | Where-Object name -eq 'Crossroads').id
$westArmy = ($state.armies | Where-Object ownerId -eq $first.playerId | Select-Object -First 1).id
$eastArmy = ($state.armies | Where-Object ownerId -eq $second.playerId | Select-Object -First 1).id
$body = @{ destinationLocationId = $crossroads } | ConvertTo-Json
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$westArmy/move" -Headers $firstHeaders -Method Post -ContentType 'application/json' -Body $body
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$eastArmy/move" -Headers $secondHeaders -Method Post -ContentType 'application/json' -Body $body
Start-Sleep -Seconds 6
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId" -Headers $firstHeaders
$state = Invoke-RestMethod "$base/api/v1/matches/$matchId/armies/$westArmy/retreat" -Headers $firstHeaders -Method Post
$state.armies | Where-Object id -eq $westArmy
```

Successful retreat from active battle kills each living wounded unit; healthy units survive and travel normally.
If no living unit remains, the army is destroyed without travel. At actual battle termination, wounded living units of
participating armies remaining at the location immediately heal to full HP. Dead units never recover. This is a one-time
domain transition, not regeneration; retreat rejection adds no casualties. Snapshots/details expose the persisted result,
and existing coarse battle/retreat/destruction events request client resync without a continuous HP feed.
See [domain combat rules](../ConquestusTerrarum.Domain/World/COMBAT.md). Illegal actions return 409. Retreat and
development creation accept no body. Move accepts only
`destinationLocationId`; missing, malformed, empty or extra fields are rejected. These routes accept no query
parameters, including command timestamps.

Stop the API with Ctrl+C, then `docker compose stop` to retain database data.

## Contract and transactions

The snapshot, updates, move, retreat, and army-details routes require `Authorization: Bearer <playerToken>`.
One endpoint filter resolves the authenticated player from a match-scoped SHA-256 token hash in PostgreSQL.
Missing, malformed, invalid, or another match's credentials return 401 with a Bearer challenge. A nonempty valid-format
token targeting a missing match returns 404. The authenticated player is available in request-scoped `MatchPlayer` for
future per-player read projections. Both players currently see the full map, global feed, and army details; no fog of war.
Move and retreat check `army.OwnerId` inside the existing locked match transaction and return 403 for another player's army.
Rejected commands roll back lazy advancement, snapshot/version changes, and feed entries together.

Session creation inserts the authoritative match and both player slots in one transaction. Session metadata stays outside
WorldSnapshot: only player identities appear as army/location ownership. Tokens have 256 bits of secure random entropy,
encoded as Base64Url; PostgreSQL stores 32-byte SHA-256 hashes of that encoded credential. Joining locks the session row,
then conditionally updates only the unclaimed second slot. Database keys/checks enforce two possible slot numbers,
distinct identities within a match, hash uniqueness, and consistent claim timestamps. Codes remain unique after a match
fills, allowing repeat joins to receive 409. Join claims do not change simulation state or emit global feed entries.
These operations use PostgreSQL only, with no process-local authoritative session state or extension requirements.

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

Handled errors use application/problem+json with status, title, detail, type, code and traceId. Authentication is 401;
foreign-army commands are 403; missing match or unknown join code is 404; a full match is 409;
invalid input is 400 (unsupported content type can be 415); illegal gameplay is 409; database clock behind the
checkpoint or transient database failure is 503; corrupt state and unexpected failures are 500 with sanitized details.
Exception stack traces stay out of responses.

## Tests and later work

Conquestus Terrarum is a persistent strategy world. Current participation lifecycle is
**WaitingForPlayers -> Active**. Active is open-ended: losing one player's last army, or every army in the world,
does not end the world. There is no winner or draw result. Recruitment, economy and territory survival rules are
future systems, not implemented features.

Production state includes camel-case `phase` (`waitingForPlayers`, `active`). Participation comes only from PostgreSQL
player-slot claims; armies/HP do not determine phase. Credentials never enter WorldSnapshot, and no schema change is
needed. Join takes the match row lock and commits the slot claim, one `MatchStarted` event, row version and cursor
atomically. The creator learns activation through the existing feed/resync flow without manual Refresh.
Move/retreat require Active inside the match transaction (409 `match_waiting_for_players`); ownership remains 403 and
ordinary gameplay conflicts remain 409. Rejected commands roll back all advancement/version/feed changes.
After elimination, survivors may still receive valid commands and both players may read/reconnect with their original
credentials. Empty worlds remain Active once both slots are claimed.

Lazy simulation and meaningful army/battle updates continue unchanged, without a global finish event. Previously
persisted obsolete event kinds remain readable as ordinary JSON feed entries. Unity treats unknown kinds safely by
loading a full authoritative snapshot before acknowledging that page; old feed data cannot control participation.

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

Player-specific visibility, command idempotency/retry policy and transport deployment
remain later work. The existing cursor-based updates route and focused army-details polling are preserved;
continuous HP does not generate global feed entries or force detail-poll snapshot writes. There is no background simulation
or push service. A connection failure during
commit can leave the caller uncertain whether a command committed; commands are not retried automatically. Gameplay is
unchanged, including the unresolved ownership rule after mutual annihilation.
