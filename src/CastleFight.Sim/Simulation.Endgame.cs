using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// After a castle falls the match winds down: buildings stop producing, nobody fights any more, the builders down
/// tools and income stops. The fallen castle is torn down for <see cref="TeardownTicks"/> (an animation later; for
/// now everyone just waits), then it becomes a round hole. Every unit on the map walks to the hole and drops in;
/// <see cref="HoleDrops"/> counts them.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>How long the fallen castle takes to be torn down before the hole opens (5 s).</summary>
    public const int TeardownTicks = 5 * TicksPerSecond;

    /// <summary>Units this close to the hole walk straight at it instead of following the flow field.</summary>
    static readonly Fix HoleApproachDistance = Fix.FromInt(6);

    BaseFlowField? _holeField;

    /// <summary>Tick on which the castle fell, or -1.</summary>
    public int GameOverTick { get; private set; } = -1;

    /// <summary>Centre of the fallen castle: where the hole opens.</summary>
    public FixVec2 HolePosition { get; private set; }

    /// <summary>The hole is as wide as the castle was.</summary>
    public Fix HoleRadius => Catalog[Catalog.CastleTypeId].Radius;

    public bool IsHoleOpen => _holeField is not null;

    /// <summary>Units that have dropped into the hole so far.</summary>
    public int HoleDrops { get; private set; }

    /// <summary>The castle in <paramref name="fallenCastle"/> was just destroyed (it is freed right after this).</summary>
    void EndGame(int fallenCastle)
    {
        var u = Units;
        WinningTeam = 1 - u.Team[fallenCastle];
        GameOverTick = Tick;
        HolePosition = u.Pos[fallenCastle];

        foreach (var player in _players)
        {
            player.Builder.Tasks.Clear();
            StopBuilder(player);
        }
        Array.Fill(u.Target, -1, 0, u.HighWater);
    }

    void OpenHole()
    {
        _holeField = BaseFlowField.ToHole(Map, WinningTeam, Map.CastleFootprint(1 - WinningTeam));
        _holeField.Rebuild(_tileOccupant);
    }

    /// <summary>Follows the flow field around walls and buildings, and walks straight in for the last few tiles.</summary>
    FixVec2 HoleHeading(FixVec2 position)
    {
        FixVec2 toHole = HolePosition - position;
        Fix distanceSq = toHole.LengthSquared;
        if (distanceSq > HoleApproachDistance * HoleApproachDistance)
        {
            FixVec2 flow = _holeField!.DirectionAt(position);
            if (!flow.IsZero)
                return flow;
        }
        return distanceSq.Raw == 0 ? FixVec2.Zero : toHole / Fix.Sqrt(distanceSq);
    }

    /// <summary>Every unit whose centre is over the hole drops in and is gone.</summary>
    void DropIntoHole()
    {
        var u = Units;
        Fix radiusSq = HoleRadius * HoleRadius;
        for (int i = 0; i < u.HighWater; i++)
        {
            if (!u.Alive[i] || u.IsStructure[i] || FixVec2.DistanceSquared(u.Pos[i], HolePosition) >= radiusSq)
                continue;
            u.Free(i);
            HoleDrops++;
        }
    }
}
