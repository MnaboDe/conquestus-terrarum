using System.Text.Json.Nodes;
using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Persistence.Tests;

public class WorldSnapshotSerializerTests
{
    private readonly WorldSnapshotSerializer _serializer = new();

    [Fact]
    public void JsonRoundTripPreservesCompleteSnapshotAndFutureSimulation()
    {
        var world = Combat();
        var json = _serializer.Serialize(world.CreateSnapshot());
        var restored = WorldState.Restore(_serializer.Deserialize(json));
        Assert.Equal(json, _serializer.Serialize(restored.CreateSnapshot()));
        foreach (var time in new[] { 1, 3, 10, 30, 60 })
        {
            WorldSimulator.AdvanceTo(world, Start.AddSeconds(time));
            WorldSimulator.AdvanceTo(restored, Start.AddSeconds(time));
            Assert.Equal(_serializer.Serialize(world.CreateSnapshot()),
                _serializer.Serialize(restored.CreateSnapshot()));
        }
    }

    [Fact]
    public void JsonUsesTickTravelTimesAndPreservesDecimalExtremesAndUnsignedSeed()
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Home"), new Location(Id(2), "Next")],
                [new MapConnection(Id(1), Id(2), TimeSpan.FromTicks(123456789))]),
            [Army(11, 21, 1, Unit(101, decimal.MaxValue, 0.0000000000000000000000000001m))], Start,
            randomSeed: ulong.MaxValue);
        var json = _serializer.Serialize(world.CreateSnapshot());
        Assert.Contains("\"travelTimeTicks\":123456789", json);
        var snapshot = _serializer.Deserialize(json);
        Assert.Equal(decimal.MaxValue, snapshot.Armies[0].Units[0].CurrentHitPoints);
        Assert.Equal(0.0000000000000000000000000001m, snapshot.Armies[0].Units[0].DamagePerSecond);
        Assert.Equal(ulong.MaxValue, snapshot.RandomSeed);
        Assert.Equal(Start.Offset, snapshot.CheckpointTime.Offset);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"formatVersion\":99}")]
    public void InvalidJsonProducesClearPersistenceException(string json)
    {
        Assert.Throws<InvalidWorldStateException>(() => _serializer.Deserialize(json));
    }

    [Theory]
    [InlineData("randomSeed")]
    [InlineData("checkpointTime")]
    [InlineData("battles")]
    public void MissingStateIsRejectedInsteadOfDefaulted(string property)
    {
        var node = JsonNode.Parse(_serializer.Serialize(Combat().CreateSnapshot()))!.AsObject();
        node.Remove(property);
        Assert.Throws<InvalidWorldStateException>(() => _serializer.Deserialize(node.ToJsonString()));
    }

    [Fact]
    public void InvalidDomainStateIsReportedAsCorruptSnapshot()
    {
        var node = JsonNode.Parse(_serializer.Serialize(Combat().CreateSnapshot()))!;
        node["battles"] = new JsonArray();
        Assert.Throws<InvalidWorldStateException>(() => _serializer.Deserialize(node.ToJsonString()));
    }

    [Fact]
    public void NullCollectionElementsAreRejected()
    {
        var node = JsonNode.Parse(_serializer.Serialize(Combat().CreateSnapshot()))!;
        node["armies"]!.AsArray().Add(null);
        Assert.Throws<InvalidWorldStateException>(() => _serializer.Deserialize(node.ToJsonString()));
    }

    [Fact]
    public void RetreatLockSurvivesJsonRoundTrip()
    {
        var world = Retreat(true);
        world.Retreat(Id(11), Start.AddSeconds(1));
        var restored = WorldState.Restore(_serializer.Deserialize(_serializer.Serialize(world.CreateSnapshot())));
        WorldSimulator.AdvanceTo(restored, Start.AddSeconds(2));
        Assert.False(restored.GetArmy(Id(11)).CanRetreat);
        Assert.Throws<InvalidOperationException>(() => restored.Retreat(Id(11), Start.AddSeconds(2)));
    }
}