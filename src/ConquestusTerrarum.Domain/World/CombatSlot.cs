using System.Numerics;

namespace ConquestusTerrarum.Domain.World;

/// <summary>A pairing's immutable anchors make damage independent of checkpoint frequency.</summary>
public sealed partial class CombatSlot
{
    internal CombatSlot(int index, Army firstArmy, Unit first, Army secondArmy, Unit second, DateTimeOffset pairedAt)
    {
        Index = index;
        FirstArmyId = firstArmy.Id;
        SecondArmyId = secondArmy.Id;
        FirstUnit = first;
        SecondUnit = second;
        PairedAt = pairedAt;
        FirstStartingHitPoints = first.CurrentHitPoints;
        SecondStartingHitPoints = second.CurrentHitPoints;
        FirstDeathAt = DeathAt(pairedAt, FirstStartingHitPoints, second.DamagePerSecond);
        SecondDeathAt = DeathAt(pairedAt, SecondStartingHitPoints, first.DamagePerSecond);
    }

    public int Index { get; }
    public Guid FirstArmyId { get; }
    public Guid SecondArmyId { get; }
    public Guid FirstUnitId => FirstUnit.Id;
    public Guid SecondUnitId => SecondUnit.Id;
    public DateTimeOffset PairedAt { get; }
    public decimal FirstStartingHitPoints { get; }
    public decimal SecondStartingHitPoints { get; }
    public DateTimeOffset? FirstDeathAt { get; }
    public DateTimeOffset? SecondDeathAt { get; }

    public DateTimeOffset? NextDeathAt => FirstDeathAt is null ? SecondDeathAt :
        SecondDeathAt is null ? FirstDeathAt :
        FirstDeathAt < SecondDeathAt ? FirstDeathAt : SecondDeathAt;

    internal Unit FirstUnit { get; }
    internal Unit SecondUnit { get; }

    internal void AdvanceDamage(DateTimeOffset time)
    {
        var ticks = (time - PairedAt).Ticks;
        FirstUnit.SetCombatHitPoints(FirstDeathAt <= time
            ? 0
            : RemainingHitPoints(FirstStartingHitPoints, SecondUnit.DamagePerSecond, ticks));
        SecondUnit.SetCombatHitPoints(SecondDeathAt <= time
            ? 0
            : RemainingHitPoints(SecondStartingHitPoints, FirstUnit.DamagePerSecond, ticks));
    }

    private static decimal RemainingHitPoints(decimal hp, decimal dps, long ticks)
    {
        var (h, hs) = DecimalParts(hp);
        var (d, ds) = DecimalParts(dps);
        var numerator = h * BigInteger.Pow(10, ds) * TimeSpan.TicksPerSecond -
                        d * ticks * BigInteger.Pow(10, hs);
        if (numerator <= 0) return 0;
        var denominator = BigInteger.Pow(10, hs + ds) * TimeSpan.TicksPerSecond;
        // Round toward positive HP: decimal precision must never kill a unit early.
        // Anchors ensure this rounding cannot accumulate across checkpoints.
        for (var scale = 28; scale >= 0; scale--)
        {
            var coefficient = (numerator * BigInteger.Pow(10, scale) + denominator - 1) / denominator;
            if (coefficient > (BigInteger.One << 96) - 1) continue;
            return new decimal((int)(uint)(coefficient & uint.MaxValue),
                (int)(uint)((coefficient >> 32) & uint.MaxValue),
                (int)(uint)((coefficient >> 64) & uint.MaxValue), false, (byte)scale);
        }

        throw new InvalidOperationException("Remaining HP exceeds decimal range.");
    }

    private static DateTimeOffset? DeathAt(DateTimeOffset start, decimal hp, decimal dps)
    {
        if (dps == 0) return null;
        start = start.ToUniversalTime();
        var (hpValue, hpScale) = DecimalParts(hp);
        var (dpsValue, dpsScale) = DecimalParts(dps);
        var numerator = hpValue * BigInteger.Pow(10, dpsScale) * TimeSpan.TicksPerSecond;
        var denominator = dpsValue * BigInteger.Pow(10, hpScale);
        // MVP minimum time resolution: one TimeSpan tick (100 ns).
        // Exact integer ceiling never schedules death early. Distinct mathematical
        // deaths rounding to the same representable tick resolve simultaneously.
        var ticks = (numerator + denominator - 1) / denominator;
        var remaining = (DateTimeOffset.MaxValue - start).Ticks;
        return ticks > remaining ? null : start.AddTicks((long)ticks);
    }

    private static (BigInteger Value, int Scale) DecimalParts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var integer = (uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        return (integer, (bits[3] >> 16) & 0xff);
    }
}