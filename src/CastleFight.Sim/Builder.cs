using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public enum BuilderOrder : byte
{
    Idle,

    /// <summary>Walking to <see cref="Builder.Destination"/>.</summary>
    Move,

    /// <summary>Walking to the middle of the site to build <see cref="Builder.BuildTypeId"/> there.</summary>
    GoToSite,

    /// <summary>Standing on the site; the building is done after <see cref="Simulation.BuildTicks"/>.</summary>
    Construct,
}

/// <summary>
/// The one worker every player has. It walks in a straight line over everything (it does not collide and
/// cannot be attacked), and builds by standing on the site for <see cref="Simulation.BuildTicks"/>. Gold is paid
/// when construction starts and refunded if the builder is ordered away before it is done.
/// </summary>
public sealed class Builder
{
    internal Builder(FixVec2 position)
    {
        Pos = position;
        PrevPos = position;
        Destination = position;
    }

    public FixVec2 Pos { get; internal set; }

    /// <summary>Position at the start of the last tick, for render interpolation.</summary>
    public FixVec2 PrevPos { get; internal set; }

    public FixVec2 Destination { get; internal set; }
    public BuilderOrder Order { get; internal set; }

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
}
