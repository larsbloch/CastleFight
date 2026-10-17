using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Bots;

/// <summary>
/// Placeholder opponent: every two seconds, if its builder is free, it sends it to build a random building in its zones.
/// It only reads the simulation and produces commands, exactly like a human player's input.
/// </summary>
public sealed class ScriptedBot
{
    const int ThinkIntervalTicks = Simulation.TicksPerSecond * 2;
    const int PlacementAttempts = 24;

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
        if (player.Builder.IsBusy || player.Gold < sim.Catalog[typeId].Cost)
            return false;

        var zones = sim.Map.BuildZones(player.Team);
        int totalArea = 0;
        foreach (var z in zones)
            totalArea += z.Width * z.Height;
        int size = sim.Catalog[typeId].FootprintTiles;
        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            // Bigger zones get picked more often, so buildings spread evenly over all of them.
            int pick = _rng.NextInt(totalArea);
            TileRect zone = zones[0];
            foreach (var z in zones)
            {
                zone = z;
                pick -= z.Width * z.Height;
                if (pick < 0)
                    break;
            }
            int tileX = zone.MinX + _rng.NextInt(zone.Width - size + 1);
            int tileY = zone.MinY + _rng.NextInt(zone.Height - size + 1);
            if (sim.CanPlaceBuilding(PlayerId, typeId, tileX, tileY))
            {
                command = Command.Build(PlayerId, (ushort)typeId, tileX, tileY);
                return true;
            }
        }
        return false;
    }
}
