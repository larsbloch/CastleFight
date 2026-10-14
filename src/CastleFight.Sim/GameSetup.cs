namespace CastleFight.Sim;

/// <summary>Everything every client must agree on before tick 0.</summary>
public sealed record GameSetup(ulong Seed, IReadOnlyList<byte> PlayerTeams)
{
    public int StartingGold { get; init; } = 300;
    public int IncomePerSecond { get; init; } = 10;

    /// <summary>Players 0..n-1 on team 0, n..2n-1 on team 1.</summary>
    public static GameSetup ForTeams(ulong seed, int playersPerTeam) =>
        new(seed, Enumerable.Range(0, playersPerTeam * 2).Select(i => (byte)(i / playersPerTeam)).ToArray());
}

public sealed class Player
{
    internal Player(int id, byte team, int gold)
    {
        Id = id;
        Team = team;
        Gold = gold;
    }

    public int Id { get; }
    public byte Team { get; }
    public int Gold { get; internal set; }
}
