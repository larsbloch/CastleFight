using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>All unit and building types in a match, indexed by type id.</summary>
public sealed class UnitCatalog
{
    public const ushort Castle = 0;
    public const ushort Barracks = 1;
    public const ushort ArcheryRange = 2;
    public const ushort Kennel = 3;
    public const ushort HuntingLodge = 4;
    public const ushort Footman = 5;
    public const ushort Archer = 6;
    public const ushort Skirmisher = 7;
    public const ushort Hunter = 8;

    readonly UnitType[] _types;

    public UnitCatalog(IEnumerable<UnitType> types)
    {
        _types = types.ToArray();
        foreach (var type in _types)
        {
            bool validFootprint = type.IsStructure
                ? type.FootprintTiles >= 1 && type.Radius * 2 >= Fix.FromInt(type.FootprintTiles)
                : type.FootprintTiles == 0;
            if (!validFootprint)
                throw new ArgumentException($"{type.Name}: structures need a footprint of at least 1 tile that fits inside 2 x Radius; units have none.", nameof(types));
        }
        CastleTypeId = Array.FindIndex(_types, t => t.IsCastle);
        if (CastleTypeId < 0)
            throw new ArgumentException("The catalog needs a castle type.", nameof(types));

        BuildableTypeIds = Enumerable.Range(0, _types.Length)
            .Where(id => _types[id] is { IsStructure: true, IsCastle: false, Cost: > 0 })
            .ToArray();
        MobileTypeIds = Enumerable.Range(0, _types.Length).Where(id => !_types[id].IsStructure).ToArray();
        MaxRadius = _types.Aggregate(Fix.Zero, (max, t) => Fix.Max(max, t.Radius));
        MaxMobileRadius = MobileTypeIds.Aggregate(Fix.Zero, (max, id) => Fix.Max(max, _types[id].Radius));
    }

    public int Count => _types.Length;
    public UnitType this[int typeId] => _types[typeId];

    public int CastleTypeId { get; }
    public IReadOnlyList<int> BuildableTypeIds { get; }
    public IReadOnlyList<int> MobileTypeIds { get; }
    public Fix MaxRadius { get; }
    public Fix MaxMobileRadius { get; }

    public static UnitCatalog CreateDefault() => new(
    [
        new UnitType
        {
            Name = "Castle", IsStructure = true, IsCastle = true,
            MaxHp = 6000, Radius = Tiles(25), FootprintTiles = 4,
            Damage = 40, AttackRange = Tiles(50), AggroRange = Tiles(60), AttackCooldownTicks = Seconds(10),
        },
        new UnitType
        {
            Name = "Barracks", IsStructure = true, MaxHp = 800, Radius = Tiles(12), FootprintTiles = 2, Cost = 100,
            SpawnTypeId = Footman, SpawnIntervalTicks = Seconds(50),
        },
        new UnitType
        {
            Name = "Archery Range", IsStructure = true, MaxHp = 650, Radius = Tiles(12), FootprintTiles = 2, Cost = 120,
            SpawnTypeId = Archer, SpawnIntervalTicks = Seconds(60),
        },
        new UnitType
        {
            Name = "Kennel", IsStructure = true, MaxHp = 600, Radius = Tiles(11), FootprintTiles = 2, Cost = 90,
            SpawnTypeId = Skirmisher, SpawnIntervalTicks = Seconds(35),
        },
        new UnitType
        {
            Name = "Hunting Lodge", IsStructure = true, MaxHp = 700, Radius = Tiles(12), FootprintTiles = 2, Cost = 140,
            SpawnTypeId = Hunter, SpawnIntervalTicks = Seconds(60),
        },
        new UnitType
        {
            Name = "Footman", MaxHp = 220, Radius = Tiles(4), Speed = TilesPerSecond(20),
            Damage = 14, AttackRange = Tiles(2), AggroRange = Tiles(60), AttackCooldownTicks = Seconds(10),
        },
        new UnitType
        {
            Name = "Archer", MaxHp = 120, Radius = Tiles(3), Speed = TilesPerSecond(18),
            Damage = 11, AttackRange = Tiles(50), AggroRange = Tiles(65), AttackCooldownTicks = Seconds(12),
        },
        new UnitType
        {
            Name = "Skirmisher", Behavior = UnitBehavior.Spreader, MaxHp = 130, Radius = Tiles(3), Speed = TilesPerSecond(26),
            Damage = 8, AttackRange = Tiles(2), AggroRange = Tiles(50), AttackCooldownTicks = Seconds(7),
            SpreadFactor = Fix.FromRatio(6, 10),
        },
        new UnitType
        {
            Name = "Hunter", Behavior = UnitBehavior.Hunter, MaxHp = 180, Radius = Tiles(4), Speed = TilesPerSecond(24),
            Damage = 20, AttackRange = Tiles(2), AggroRange = Tiles(90), AttackCooldownTicks = Seconds(11),
            PreferredTargetTypeId = Archer,
        },
    ]);

    /// <summary>Distance in tenths of a tile.</summary>
    public static Fix Tiles(int tenths) => Fix.FromRatio(tenths, 10);

    /// <summary>Speed in tenths of a tile per second, converted to tiles per tick.</summary>
    public static Fix TilesPerSecond(int tenths) => Fix.FromRatio(tenths, 10 * Simulation.TicksPerSecond);

    /// <summary>Duration in tenths of a second, converted to ticks.</summary>
    public static int Seconds(int tenths) => tenths * Simulation.TicksPerSecond / 10;
}
