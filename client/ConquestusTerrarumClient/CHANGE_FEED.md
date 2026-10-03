# Change-feed application and manual validation

The client applies an entire page on detached DTOs through `MatchUpdateApplier`.
Only successful pages advance `updateCursor`; pages included in a newer command
snapshot are ignored. Gaps, malformed payloads and unknown events request one full
GET for the page. A failed resync remains pending, so its events are not lost.
Background replies from an older command/state generation or another match are ignored.
Global feed and selected-army detail requests do not overlap.

Locally supported events:

- ArmyMovementStarted: validate stationary army, owner, route and server timestamps;
  preserve movement interpolation anchors.
- ArmyArrived: validate moving army, destination, movement kind and arrival timestamp;
  clear movement, set location/previous location and use the existing authoritative
  ArriveAt anchor for enteredLocationAt. Arrivals at an active battle or a destination
  occupied by another owner conservatively resync, including allies. The client does
  not reconstruct combat/retreat state from an arrival hint.
- LocationOwnerChanged: validate location and previous owner before replacing owner.

BattleStarted/BattleEnded currently carry only locationId; ArmyDestroyed lacks unit
state. They intentionally require a full snapshot. No battle slots, casualties,
retreat flags or ownership rules are invented locally. ArmyRetreatStarted also needs
resync because its payload omits casualties. There are no new global HP events.

Selected battle-army details refresh units only while structural flags agree with
local state. A structural change requires resync because details lack complete map
and battle state. The backend QueryCurrentAsync remains unchanged: continuous damage
is calculated lazily without rewriting the match; generated coarse transitions and
the resulting snapshot are committed atomically.

## Automated checks

The actual Unity-independent DTO/applier sources are linked into the API test project
solely for tests. Unity has no server assembly/project reference. These are xUnit tests,
not Unity EditMode tests; they cover movement/arrival/owner batches, retreat arrival,
rollback of unsafe pages, cursor replay/gaps and inconsistent/unknown events.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\ConquestusTerrarum.sln
& 'C:\Program Files\dotnet\dotnet.exe' test .\tests\ConquestusTerrarum.Api.Tests
$env:CT_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
& 'C:\Program Files\dotnet\dotnet.exe' test .\ConquestusTerrarum.sln --filter Category=Postgres
```

PostgreSQL coverage includes repeated HTTP detail reads checking unchanged JSON,
version, updated_at, sequence, row xmin and feed count; live HP must decrease. It also
covers atomic transition persistence and rollback when the query callback throws.

## Manual Unity test plan

Start PostgreSQL/migrations/API as described in `src/ConquestusTerrarum.Api/README.md`,
open SampleScene and press Play. Record the match ID. Observe API request logs (full
GET versus /updates and /details); use an external PowerShell request to issue moves
for the same match, so events are foreign to this client rather than already included
in its own command response. Keep requests directed at this disposable local match.

1. **Foreign ArmyMovementStarted:** externally move a stationary army along a connected
   edge. On the next /updates poll its marker must interpolate with the supplied
   StartedAt/ArriveAt. No full GET should be issued for this page alone.
2. **ArmyArrived without combat:** move to an empty location and let /updates wake lazy
   simulation. The marker becomes stationary, movement clears, previousLocationId is
   the origin and enteredLocationAt matches ArriveAt. Any ownership event in that page
   applies sequentially; no full GET is needed.
3. **ArmyArrived + BattleStarted:** move hostile armies to one location. The entire
   unsafe page must cause one full GET, showing authoritative flags, slots and battle
   marker. No partial arrival/owner page is published before the snapshot.
4. **BattleEnded:** let the battle finish, or retreat when eligible. Expect one full
   GET for a page with BattleEnded, regardless of accompanying destruction/owner events.
   Verify battle marker and retreat flags match the returned snapshot.
5. **ArmyDestroyed:** let an army lose all units. Expect resync, disappearance of its
   marker and authoritative zero/dead units in the HUD, without invented unit HP.
6. **Unknown/inconsistent event:** use a local mock/proxy feed response with an unknown
   type, missing army, wrong destination, wrong owner or sequence gap. Include several
   unsafe events on one page. Verify a single full GET, no premature cursor advancement,
   and no partial state change. Temporarily fail this GET: pending resync should remain
   and succeed later; the unsafe page must not be silently acknowledged.
7. **Battle HP detail polling:** select an army in battle. HP should refresh through
   /details approximately every 0.75 s, while /updates remains coarse (about every 2 s).
   Pure continuous damage must not grow version/feed or rewrite the match row. A real
   structural transition may commit and trigger resync.
8. **Delayed responses:** delay a feed/detail response, issue a command or New Demo Match,
   then release it. It must not overwrite the command response or the new match state.

Unity batch compilation verifies C# compatibility, not these interactive/network
scenarios. Verify those in Play Mode before merging.
