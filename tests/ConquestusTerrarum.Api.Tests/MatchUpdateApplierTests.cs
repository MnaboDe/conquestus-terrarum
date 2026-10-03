using ConquestusTerrarum.Client;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

// Compiles the actual Unity-independent client source, without Unity or HTTP.
public sealed class MatchUpdateApplierTests
{
    private static string Key(int id) => Id(id).ToString();
    private static string Time(int seconds) => Start.AddSeconds(seconds).ToString("O");
    private static MatchStateDto World() => new()
    {
        matchId = Key(90), version = 7, updateCursor = 10, serverTime = Time(0), checkpointTime = Time(0),
        locations = [new() { id = Key(1) }, new() { id = Key(2) }],
        connections = [new() { fromLocationId = Key(1), toLocationId = Key(2), travelTimeTicks = TimeSpan.FromSeconds(5).Ticks }],
        activeBattles = [], alliances = [],
        armies = [new() { id = Key(11), ownerId = Key(21), locationId = Key(1), enteredLocationAt = Time(0),
            units = [new() { id = Key(101), maxHitPoints = 100, currentHitPoints = 100, isAlive = true }] }]
    };
    private static MatchUpdateDto Movement(long sequence = 11) => new()
    {
        sequence = sequence, type = "ArmyMovementStarted", occurredAt = Time(1),
        payload = new() { armyId = Key(11), ownerId = Key(21), fromLocationId = Key(1), toLocationId = Key(2), startedAt = Time(1), arriveAt = Time(6) }
    };
    private static MatchUpdateDto Arrival(long sequence = 12) => new()
    {
        sequence = sequence, type = "ArmyArrived", occurredAt = Time(6),
        payload = new() { armyId = Key(11), ownerId = Key(21), locationId = Key(2), movementKind = "Normal" }
    };
    private static MatchUpdateDto Owner(long sequence = 13) => new()
    {
        sequence = sequence, type = "LocationOwnerChanged", occurredAt = Time(6),
        payload = new() { locationId = Key(2), previousOwnerId = null!, ownerId = Key(21) }
    };
    private static MatchUpdatesDto Page(params MatchUpdateDto[] events) => new()
    { nextCursor = events.Length == 0 ? 10 : events[^1].sequence, updates = events };

    [Fact]
    public void ForeignMovementThenArrivalAndOwnershipApplyInOrderWithoutChangingHpOrClock()
    {
        var before = World();
        Assert.True(MatchUpdateApplier.TryApplyPage(before, Page(Movement(), Arrival(), Owner()), 10, out var after, out var cursor));
        Assert.NotSame(before, after);
        Assert.Equal(13, cursor); Assert.Equal(cursor, after.updateCursor);
        var army = after.armies.Single();
        Assert.False(army.isMoving); Assert.Null(army.movement);
        Assert.Equal(Key(2), army.locationId); Assert.Equal(Key(1), army.previousLocationId);
        Assert.Equal(Time(6), army.enteredLocationAt);
        Assert.False(army.isInBattle); Assert.False(army.canRetreat); Assert.Null(army.retreatLocationId);
        Assert.Equal(Key(21), after.locations[1].ownerId);
        Assert.Same(before.armies[0].units, army.units);
        Assert.Equal(before.version, after.version); Assert.Equal(before.serverTime, after.serverTime);
        Assert.Equal(before.checkpointTime, after.checkpointTime);
        Assert.Equal(Key(1), before.armies[0].locationId); Assert.Null(before.locations[1].ownerId);
    }

    [Fact]
    public void MovementAlonePreservesInterpolationAnchors()
    {
        Assert.True(MatchUpdateApplier.TryApplyPage(World(), Page(Movement()), 10, out var after, out _));
        var army = after.armies.Single();
        Assert.True(army.isMoving); Assert.Null(army.locationId); Assert.Null(army.enteredLocationAt);
        Assert.Equal(Time(1), army.movement.startedAt); Assert.Equal(Time(6), army.movement.arriveAt);
        Assert.Equal("normal", army.movement.kind);
    }

    [Theory]
    [InlineData("normal", "Normal")]
    [InlineData("retreat", "Retreat")]
    public void UncontestedArrivalUsesExactMovementAnchorAndPreservesUnits(string localKind, string payloadKind)
    {
        Assert.True(MatchUpdateApplier.TryApplyPage(World(), Page(Movement()), 10, out var moving, out _));
        moving.armies[0].movement.kind = localKind;
        var arrival = Arrival(); arrival.payload.movementKind = payloadKind;
        Assert.True(MatchUpdateApplier.TryApplyPage(moving, Page(arrival), 11, out var arrived, out _));
        Assert.Equal(Time(6), arrived.armies[0].enteredLocationAt);
        Assert.Equal(Key(1), arrived.armies[0].previousLocationId);
        Assert.Same(moving.armies[0].units, arrived.armies[0].units);
    }

    [Theory]
    [InlineData("BattleStarted")]
    [InlineData("BattleEnded")]
    [InlineData("ArmyDestroyed")]
    [InlineData("ArmyRetreatStarted")]
    [InlineData("NewUnknownType")]
    public void AnyInsufficientEventRequiresOnePageResyncAndAcknowledgesNothing(string type)
    {
        var before = World();
        var incomplete = new MatchUpdateDto { sequence = 13, occurredAt = Time(6), type = type,
            payload = new() { locationId = Key(2), armyId = Key(11) } };
        var page = Page(Movement(), Arrival(), incomplete, Owner(14));
        Assert.False(MatchUpdateApplier.TryApplyPage(before, page, 10, out var after, out var cursor));
        Assert.Same(before, after); Assert.Equal(10, cursor);
        Assert.Equal(Key(1), before.armies[0].locationId); Assert.Null(before.locations[1].ownerId);
        Assert.Equal(100, before.armies[0].units[0].currentHitPoints);
    }

    [Theory]
    [InlineData("not-moving")]
    [InlineData("destination")]
    [InlineData("owner")]
    [InlineData("kind")]
    [InlineData("timestamp")]
    [InlineData("missing-payload")]
    [InlineData("missing-army")]
    [InlineData("destroyed")]
    [InlineData("battle")]
    [InlineData("occupied")]
    [InlineData("retreat-flags")]
    public void InconsistentArrivalDoesNotPartiallyMutateState(string fault)
    {
        Assert.True(MatchUpdateApplier.TryApplyPage(World(), Page(Movement()), 10, out var before, out _));
        var arrival = Arrival();
        switch (fault)
        {
            case "not-moving": before.armies[0].isMoving = false; break;
            case "destination": arrival.payload.locationId = Key(1); break;
            case "owner": arrival.payload.ownerId = Key(22); break;
            case "kind": arrival.payload.movementKind = "Retreat"; break;
            case "timestamp": arrival.occurredAt = Time(7); break;
            case "missing-payload": arrival.payload = null!; break;
            case "missing-army": arrival.payload.armyId = Key(99); break;
            case "destroyed": before.armies[0].isDestroyed = true; break;
            case "battle": before.activeBattles = [new() { locationId = Key(2) }]; break;
            case "occupied": before.armies = [before.armies[0], new() { id = Key(12), ownerId = Key(22), locationId = Key(2) }]; break;
            case "retreat-flags": before.armies[0].canRetreat = true; break;
        }
        Assert.False(MatchUpdateApplier.TryApplyPage(before, Page(arrival), 11, out var after, out var cursor));
        Assert.Same(before, after); Assert.Equal(11, cursor); Assert.NotNull(before.armies[0].movement);
    }

    [Theory]
    [InlineData("sequence-gap")]
    [InlineData("reverse-order")]
    [InlineData("cursor-ahead")]
    [InlineData("null-event")]
    [InlineData("owner-mismatch")]
    [InlineData("bad-movement-time")]
    [InlineData("bad-route")]
    public void InvalidPageRequiresResync(string fault)
    {
        var before = World(); var page = Page(Movement(), Arrival(), Owner());
        switch (fault)
        {
            case "sequence-gap": page.updates[0].sequence = 12; break;
            case "reverse-order": page.updates = [Arrival(), Movement(), Owner()]; break;
            case "cursor-ahead": page.nextCursor = 14; break;
            case "null-event": page.updates[1] = null!; break;
            case "owner-mismatch": page.updates[2].payload.previousOwnerId = Key(22); break;
            case "bad-movement-time": page.updates[0].payload.startedAt = "not-a-date"; break;
            case "bad-route": before.connections = []; break;
        }
        Assert.False(MatchUpdateApplier.TryApplyPage(before, page, 10, out var after, out var cursor));
        Assert.Same(before, after); Assert.Equal(10, cursor);
    }

    [Fact]
    public void CommandCursorSkipsAlreadyIncludedEventsIncludingUnsupportedTypes()
    {
        var before = World(); before.updateCursor = 13;
        var eventAlreadyInSnapshot = new MatchUpdateDto { sequence = 12, type = "BattleStarted" };
        Assert.True(MatchUpdateApplier.TryApplyPage(before, Page(eventAlreadyInSnapshot), 13, out var after, out var cursor));
        Assert.Same(before, after); Assert.Equal(13, cursor);
    }

    [Fact]
    public void EmptyPageDoesNotRebuildOrAdvanceCursor()
    {
        var before = World();
        Assert.True(MatchUpdateApplier.TryApplyPage(before, Page(), 10, out var after, out var cursor));
        Assert.Same(before, after); Assert.Equal(10, cursor);
    }
}
