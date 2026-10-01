namespace ConquestusTerrarum.Domain.World;

public sealed class Unit
{
    public Guid Id { get; }
    public decimal MaxHitPoints { get; }
    public decimal CurrentHitPoints { get; private set; }
    public decimal DamagePerSecond { get; }
    public bool IsAlive => CurrentHitPoints > 0;
    public bool IsWounded => IsAlive && CurrentHitPoints < MaxHitPoints;

    public Unit(Guid id, decimal maxHitPoints, decimal damagePerSecond, decimal? currentHitPoints = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Unit id cannot be empty.", nameof(id));
        if (maxHitPoints <= 0) throw new ArgumentOutOfRangeException(nameof(maxHitPoints));
        if (damagePerSecond < 0) throw new ArgumentOutOfRangeException(nameof(damagePerSecond));
        var hp = currentHitPoints ?? maxHitPoints;
        if (hp < 0 || hp > maxHitPoints) throw new ArgumentOutOfRangeException(nameof(currentHitPoints));
        Id = id;
        MaxHitPoints = maxHitPoints;
        CurrentHitPoints = hp;
        DamagePerSecond = damagePerSecond;
    }

    internal void SetCombatHitPoints(decimal hitPoints) => CurrentHitPoints = Math.Min(CurrentHitPoints, Math.Max(0, hitPoints));
    internal void Kill() => CurrentHitPoints = 0;
}
