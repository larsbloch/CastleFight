using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public enum UnitBehavior : byte
{
    /// <summary>Walk down the lane and attack the nearest enemy in aggro range.</summary>
    Default,

    /// <summary>Drifts sideways towards a random lane position while advancing, spreading the army out.</summary>
    Spreader,

    /// <summary>Prefers <see cref="UnitType.PreferredTargetTypeId"/> when one is in aggro range.</summary>
    Hunter,
}

/// <summary>
/// Static definition of a unit or building. Durations are in ticks and speeds in tiles per tick;
/// <see cref="UnitCatalog"/> has helpers for writing them in seconds.
/// </summary>
public sealed class UnitType
{
    public required string Name { get; init; }
    public bool IsStructure { get; init; }
    public bool IsCastle { get; init; }
    public UnitBehavior Behavior { get; init; }

    public int MaxHp { get; init; }
    /// <summary>Reach and (for units) collision radius. Structures collide with their square footprint instead.</summary>
    public Fix Radius { get; init; }

    /// <summary>Structures: side of the square footprint in tiles. Zero for mobile units.</summary>
    public int FootprintTiles { get; init; }
    public Fix Speed { get; init; }

    /// <summary>Zero means the unit never attacks (most buildings).</summary>
    public int Damage { get; init; }

    /// <summary>Edge-to-edge distance at which the unit can hit its target.</summary>
    public Fix AttackRange { get; init; }

    /// <summary>Edge-to-edge distance at which the unit notices enemies.</summary>
    public Fix AggroRange { get; init; }

    public int AttackCooldownTicks { get; init; }

    /// <summary>Gold cost; buildings with a cost can be placed by players.</summary>
    public int Cost { get; init; }

    public int SpawnTypeId { get; init; } = -1;
    public int SpawnIntervalTicks { get; init; }

    public int PreferredTargetTypeId { get; init; } = -1;

    /// <summary>Spreader only: max sideways speed as a fraction of <see cref="Speed"/>.</summary>
    public Fix SpreadFactor { get; init; }
}
