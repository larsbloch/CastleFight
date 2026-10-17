using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>Everything every client must agree on before tick 0.</summary>
public sealed record GameSetup(ulong Seed, IReadOnlyList<byte> PlayerTeams)
{
    /// <summary>4v4. Normal matches are 2v2 or 3v3; 8 players is for rare big games.</summary>
    public const int MaxPlayers = 8;

    public int StartingGold { get; init; } = 300;
    public int IncomePerSecond { get; init; } = 10;
    public MapId Map { get; init; } = MapId.SingleLane;

    /// <summary>Players 0..n-1 on team 0, n..2n-1 on team 1.</summary>
    public static GameSetup ForTeams(ulong seed, int playersPerTeam, MapId map = MapId.SingleLane) =>
        new(seed, Enumerable.Range(0, playersPerTeam * 2).Select(i => (byte)(i / playersPerTeam)).ToArray()) { Map = map };
}

public sealed class Player
{
    internal Player(int id, byte team, int gold, FixVec2 builderPosition)
    {
        Id = id;
        Team = team;
        Gold = gold;
        Builder = new Builder(builderPosition);
    }

    public int Id { get; }
    public byte Team { get; }
    public int Gold { get; internal set; }
    public Builder Builder { get; }
}
