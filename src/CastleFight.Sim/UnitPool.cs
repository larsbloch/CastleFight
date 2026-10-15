using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// Structure-of-arrays storage for every unit and building. Slot indices are stable while a unit
/// lives; freed slots are reused (LIFO) and their <see cref="Generation"/> bumped, so a stored
/// (slot, generation) pair detects a target that died and was replaced.
/// Arrays may be replaced when the pool grows: re-read them after spawning.
/// </summary>
public sealed class UnitPool
{
    int[] _freeSlots = [];
    int _freeCount;

    public UnitPool(int initialCapacity = 1024) => Grow(initialCapacity);

    public int Capacity => Alive.Length;

    /// <summary>Every slot ever used is below this index; iterate [0, HighWater) and skip dead slots.</summary>
    public int HighWater { get; private set; }

    public int AliveCount { get; private set; }

    public bool[] Alive { get; private set; } = [];
    public int[] Generation { get; private set; } = [];

    /// <summary>Unique, never reused spawn number. Used to break ties deterministically.</summary>
    public int[] Id { get; private set; } = [];

    public ushort[] Type { get; private set; } = [];
    public byte[] Team { get; private set; } = [];
    public byte[] Owner { get; private set; } = [];
    public bool[] IsStructure { get; private set; } = [];
    public FixVec2[] Pos { get; private set; } = [];

    /// <summary>Position at the start of the last tick, for render interpolation.</summary>
    public FixVec2[] PrevPos { get; private set; } = [];

    public FixVec2[] Vel { get; private set; } = [];
    public Fix[] Radius { get; private set; } = [];
    public int[] Hp { get; private set; } = [];

    /// <summary>Slot of the current target, or -1. Only valid while <see cref="TargetGeneration"/> matches.</summary>
    public int[] Target { get; private set; } = [];

    public int[] TargetGeneration { get; private set; } = [];
    public int[] AttackCooldown { get; private set; } = [];
    public int[] RetargetTimer { get; private set; } = [];
    public int[] SpawnTimer { get; private set; } = [];

    /// <summary>
    /// Where across its lane (0 = low edge, 1 = high edge) a <see cref="UnitBehavior.Spreader"/> drifts to.
    /// Relative, because the lane is chosen afresh from the unit's position every tick.
    /// </summary>
    public Fix[] SpreadFraction { get; private set; } = [];

    internal ReadOnlySpan<int> FreeSlots => _freeSlots.AsSpan(0, _freeCount);

    public bool IsAlive(int slot, int generation) =>
        (uint)slot < (uint)HighWater && Alive[slot] && Generation[slot] == generation;

    public bool HasValidTarget(int slot)
    {
        int target = Target[slot];
        return target >= 0 && Alive[target] && Generation[target] == TargetGeneration[slot];
    }

    internal int Allocate()
    {
        int slot;
        if (_freeCount > 0)
        {
            slot = _freeSlots[--_freeCount];
        }
        else
        {
            if (HighWater == Capacity)
                Grow(Capacity * 2);
            slot = HighWater++;
        }

        Alive[slot] = true;
        AliveCount++;
        return slot;
    }

    internal void Free(int slot)
    {
        Alive[slot] = false;
        Generation[slot]++;
        _freeSlots[_freeCount++] = slot;
        AliveCount--;
    }

    void Grow(int capacity)
    {
        Alive = Resize(Alive, capacity);
        Generation = Resize(Generation, capacity);
        Id = Resize(Id, capacity);
        Type = Resize(Type, capacity);
        Team = Resize(Team, capacity);
        Owner = Resize(Owner, capacity);
        IsStructure = Resize(IsStructure, capacity);
        Pos = Resize(Pos, capacity);
        PrevPos = Resize(PrevPos, capacity);
        Vel = Resize(Vel, capacity);
        Radius = Resize(Radius, capacity);
        Hp = Resize(Hp, capacity);
        Target = Resize(Target, capacity);
        TargetGeneration = Resize(TargetGeneration, capacity);
        AttackCooldown = Resize(AttackCooldown, capacity);
        RetargetTimer = Resize(RetargetTimer, capacity);
        SpawnTimer = Resize(SpawnTimer, capacity);
        SpreadFraction = Resize(SpreadFraction, capacity);
        _freeSlots = Resize(_freeSlots, capacity);
    }

    static T[] Resize<T>(T[] array, int length)
    {
        Array.Resize(ref array, length);
        return array;
    }
}
