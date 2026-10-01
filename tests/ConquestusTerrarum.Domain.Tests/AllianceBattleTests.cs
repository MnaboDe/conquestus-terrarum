using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class AllianceBattleTests
{
    private static readonly DateTimeOffset Checkpoint =
        new(
            2026, 1, 1,
            10, 0, 0,
            TimeSpan.Zero);

    [Fact]
    public void DifferentPlayers_WithoutAlliance_StartBattle()
    {
        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var army1 =
            new Army(
                Guid.NewGuid(),
                Guid.NewGuid(),
                location.Id);

        var army2 =
            new Army(
                Guid.NewGuid(),
                Guid.NewGuid(),
                location.Id);

        var world =
            new WorldState(
                map,
                new[] { army1, army2 },
                Checkpoint);

        Assert.NotNull(
            world.GetBattleAt(location.Id));
    }

    [Fact]
    public void AlliedPlayers_DoNotStartBattle()
    {
        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var player1 =
            Guid.NewGuid();

        var player2 =
            Guid.NewGuid();

        var alliance =
            new PlayerAlliance(
                player1,
                player2);

        var army1 =
            new Army(
                Guid.NewGuid(),
                player1,
                location.Id);

        var army2 =
            new Army(
                Guid.NewGuid(),
                player2,
                location.Id);

        var world =
            new WorldState(
                map,
                new[] { army1, army2 },
                Checkpoint,
                new[] { alliance });

        Assert.Null(
            world.GetBattleAt(location.Id));

        Assert.Empty(
            world.Battles);
    }

    [Fact]
    public void ThreePlayers_WithoutAlliances_AreFreeForAll()
    {
        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var army1 =
            new Army(
                Guid.NewGuid(),
                Guid.NewGuid(),
                location.Id);

        var army2 =
            new Army(
                Guid.NewGuid(),
                Guid.NewGuid(),
                location.Id);

        var army3 =
            new Army(
                Guid.NewGuid(),
                Guid.NewGuid(),
                location.Id);

        var world =
            new WorldState(
                map,
                new[]
                {
                    army1,
                    army2,
                    army3
                },
                Checkpoint);

        Assert.NotNull(
            world.GetBattleAt(location.Id));

        Assert.True(
            world.AreHostile(
                army1.OwnerId,
                army2.OwnerId));

        Assert.True(
            world.AreHostile(
                army1.OwnerId,
                army3.OwnerId));

        Assert.True(
            world.AreHostile(
                army2.OwnerId,
                army3.OwnerId));
    }

    [Fact]
    public void TwoAlliesAndOneEnemy_StillHaveBattle()
    {
        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var playerA =
            Guid.NewGuid();

        var playerB =
            Guid.NewGuid();

        var playerC =
            Guid.NewGuid();

        var alliance =
            new PlayerAlliance(
                playerA,
                playerC);

        var armyA =
            new Army(
                Guid.NewGuid(),
                playerA,
                location.Id);

        var armyB =
            new Army(
                Guid.NewGuid(),
                playerB,
                location.Id);

        var armyC =
            new Army(
                Guid.NewGuid(),
                playerC,
                location.Id);

        var world =
            new WorldState(
                map,
                new[]
                {
                    armyA,
                    armyB,
                    armyC
                },
                Checkpoint,
                new[] { alliance });

        Assert.True(
            world.AreAllies(
                playerA,
                playerC));

        Assert.True(
            world.AreHostile(
                playerA,
                playerB));

        Assert.True(
            world.AreHostile(
                playerC,
                playerB));

        Assert.NotNull(
            world.GetBattleAt(location.Id));
    }

    [Fact]
    public void ArmiesOfSamePlayer_DoNotStartBattle()
    {
        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var playerId =
            Guid.NewGuid();

        var army1 =
            new Army(
                Guid.NewGuid(),
                playerId,
                location.Id);

        var army2 =
            new Army(
                Guid.NewGuid(),
                playerId,
                location.Id);

        var world =
            new WorldState(
                map,
                new[] { army1, army2 },
                Checkpoint);

        Assert.Null(
            world.GetBattleAt(location.Id));

        Assert.True(
            world.AreAllies(
                playerId,
                playerId));
    }
}