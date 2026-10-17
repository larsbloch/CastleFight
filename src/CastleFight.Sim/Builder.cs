using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public enum BuilderOrder : byte
{
    Idle,

    /// <summary>Walking to <see cref="Builder.Destination"/>.</summary>
    Move,

    /// <summary>Walking to a tile next to the site to build <see cref="Builder.BuildTypeId"/> there.</summary>
    GoToSite,

    /// <summary>Standing next to the site; the building is done after <see cref="Simulation.BuildTicks"/>.</summary>
    Construct,
}

public enum BuilderTaskKind : byte
{
    Move,
    Build,
}

/// <summary>An order waiting in the builder's queue (Shift). <see cref="Destination"/> is for moves, the rest for builds.</summary>
public readonly record struct BuilderTask(BuilderTaskKind Kind, FixVec2 Destination, int TypeId, int TileX, int TileY)
{
    public static BuilderTask Move(FixVec2 destination) => new(BuilderTaskKind.Move, destination, -1, 0, 0);

    public static BuilderTask Build(int typeId, int tileX, int tileY) => new(BuilderTaskKind.Build, default, typeId, tileX, tileY);
}

/// <summary>
/// The one worker every player has. It walks around walls, buildings and construction sites along a tile path (it
/// does not collide with units and cannot be attacked), and builds by standing next to the site for
/// <see cref="Simulation.BuildTicks"/>; that tile counts as taken while it builds. Gold is paid when construction
/// starts and refunded if the builder is ordered away before it is done. Orders given with Shift wait in
/// <see cref="Queue"/> until the current one is done.
/// </summary>
public sealed class Builder
{
    internal readonly List<FixVec2> PathPoints = new();
    internal readonly List<BuilderTask> Tasks = new();

    internal Builder(FixVec2 position)
    {
        Pos = position;
        PrevPos = position;
        Destination = position;
    }

    public FixVec2 Pos { get; internal set; }

    /// <summary>Position at the start of the last tick, for render interpolation.</summary>
    public FixVec2 PrevPos { get; internal set; }

    /// <summary>Where the current walk ends: the ordered point for a move, the tile next to the site for a build.</summary>
    public FixVec2 Destination { get; internal set; }
    public BuilderOrder Order { get; internal set; }

    /// <summary>The corners of the path still ahead, the last one being the end of the walk.</summary>
    public IReadOnlyList<FixVec2> Path => PathPoints;

    /// <summary>The orders after the current one, in the order they will be carried out.</summary>
    public IReadOnlyList<BuilderTask> Queue => Tasks;

    /// <summary>The building being walked to or constructed, or -1.</summary>
    public int BuildTypeId { get; internal set; } = -1;

    /// <summary>The site's lowest tile.</summary>
    public int BuildTileX { get; internal set; }
    public int BuildTileY { get; internal set; }

    /// <summary>Ticks spent constructing so far (see <see cref="Simulation.BuildTicks"/>).</summary>
    public int BuildProgress { get; internal set; }

    /// <summary>Tick on which the last build failed when the builder reached the site, or -1. For the UI.</summary>
    public int BuildFailedTick { get; internal set; } = -1;
    public PlacementResult BuildFailure { get; internal set; }

    public bool IsBusy => Order != BuilderOrder.Idle;

    /// <summary>The obstacle version the path was planned for; a different one means it has to be planned again.</summary>
    internal int PathVersion { get; set; }
}
