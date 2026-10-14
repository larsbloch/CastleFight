using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Bots;

/// <summary>
/// Placeholder opponent: every two seconds it tries to buy a random building in its zone.
/// It only reads the simulation and produces commands, exactly like a human player's input.
/// </summary>
public sealed class ScriptedBot
{
    const int ThinkIntervalTicks = Simulation.TicksPerSecond * 2;
    const int PlacementAttempts = 12;

    DeterministicRandom _rng;

    public ScriptedBot(int playerId, ulong seed)
    {
        PlayerId = playerId;
        _rng = new DeterministicRandom(seed ^ ((ulong)(playerId + 1) * 0x9E3779B97F4A7C15UL));
    }

    public int PlayerId { get; }

    public bool TryThink(Simulation sim, out Command command)
    {
        command = default;
        if ((sim.Tick + PlayerId * 7) % ThinkIntervalTicks != 0)
            return false;

        var buildable = sim.Catalog.BuildableTypeIds;
        int typeId = buildable[_rng.NextInt(buildable.Count)];
        var player = sim.Players[PlayerId];
        if (player.Gold < sim.Catalog[typeId].Cost)
            return false;

        FixRect zone = sim.Map.BuildZone(player.Team);
        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            var position = new FixVec2(_rng.NextFix(zone.Min.X, zone.Max.X), _rng.NextFix(zone.Min.Y, zone.Max.Y));
            if (sim.CanPlaceBuilding(PlayerId, typeId, position))
            {
                command = Command.PlaceBuilding(PlayerId, (ushort)typeId, position);
                return true;
            }
        }
        return false;
    }
}
