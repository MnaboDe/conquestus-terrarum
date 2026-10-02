using System.Buffers.Binary;

namespace ConquestusTerrarum.Domain.World;

public sealed partial class Battle
{
    private readonly List<CombatSlot> _slots = new();

    internal Battle(Guid locationId, DateTimeOffset startedAt, ulong worldSeed)
    {
        if (locationId == Guid.Empty) throw new ArgumentException("Location id cannot be empty.", nameof(locationId));
        LocationId = locationId;
        StartedAt = startedAt;
        Slots = _slots.AsReadOnly();
        // Explicit byte order and UTC ticks; no runtime hashing or process randomness.
        var bytes = locationId.ToByteArray();
        RandomState = worldSeed ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^
                      BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) ^ (ulong)startedAt.UtcTicks;
        NextRandom();
    }

    public Guid LocationId { get; }
    public DateTimeOffset StartedAt { get; }
    public ulong RandomState { get; private set; }
    public IReadOnlyList<CombatSlot> Slots { get; }

    // SplitMix64: the incrementing state is all that is needed to resume the random stream.
    private ulong NextRandom()
    {
        unchecked
        {
            RandomState += 0x9E3779B97F4A7C15UL;
            var z = RandomState;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private int Choose(int count)
    {
        // Rejection sampling avoids modulo bias.
        var bound = (ulong)count;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            value = NextRandom();
        } while (value < threshold);

        return (int)(value % bound);
    }

    internal void RemoveInvalidSlots(WorldState world)
    {
        _slots.RemoveAll(slot => !slot.FirstUnit.IsAlive || !slot.SecondUnit.IsAlive ||
                                 world.GetArmy(slot.FirstArmyId).LocationId != LocationId ||
                                 world.GetArmy(slot.SecondArmyId).LocationId != LocationId);
    }

    internal void Refill(WorldState world, DateTimeOffset time)
    {
        RemoveInvalidSlots(world);
        var busy = _slots.SelectMany(slot => new[] { slot.FirstUnitId, slot.SecondUnitId }).ToHashSet();
        var idle = world.Armies.Where(army => !army.IsDestroyed && army.LocationId == LocationId)
            .SelectMany(army => army.Units.Where(unit => unit.IsAlive && !busy.Contains(unit.Id))
                .Select(unit => (Army: army, Unit: unit)))
            .OrderBy(entry => entry.Army.Id).ThenBy(entry => entry.Unit.Id).ToList();
        var slotCount = world.Map.GetLocation(LocationId).CombatSlotCount;
        for (var index = 0; index < slotCount; index++)
        {
            if (_slots.Any(slot => slot.Index == index)) continue;
            var pairs = new List<(int First, int Second)>();
            for (var a = 0; a < idle.Count; a++)
            for (var b = a + 1; b < idle.Count; b++)
                if (world.AreHostile(idle[a].Army.OwnerId, idle[b].Army.OwnerId))
                    pairs.Add((a, b));
            if (pairs.Count == 0) break;
            var pair = pairs[Choose(pairs.Count)];
            var first = idle[pair.First];
            var second = idle[pair.Second];
            _slots.Add(new CombatSlot(index, first.Army, first.Unit, second.Army, second.Unit, time));
            idle.RemoveAt(pair.Second);
            idle.RemoveAt(pair.First);
        }

        _slots.Sort((a, b) => a.Index.CompareTo(b.Index));
    }
}