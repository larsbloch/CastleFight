using CastleFight.Sim.Bots;
using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class SimulationTests
{
    const int InputDelay = 3;

    [Fact]
    public void Same_setup_and_commands_give_identical_state_every_tick()
    {
        var first = new Match(seed: 42);
        var second = new Match(seed: 42);
        first.Schedule(10, Command.DebugSpawn(0, UnitCatalog.Footman, 300));
        second.Schedule(10, Command.DebugSpawn(0, UnitCatalog.Footman, 300));
        first.Schedule(10, Command.DebugSpawn(3, UnitCatalog.Archer, 300));
        second.Schedule(10, Command.DebugSpawn(3, UnitCatalog.Archer, 300));

        for (int tick = 0; tick < 1200; tick++)
        {
            first.Step();
            second.Step();
            if (tick % 10 == 0)
                Assert.Equal(first.Sim.ComputeStateHash(), second.Sim.ComputeStateHash());
        }
        Assert.True(first.Sim.Units.AliveCount > 2);
    }

    [Fact]
    public void A_different_command_changes_the_state_hash()
    {
        var first = new Match(seed: 42);
        var second = new Match(seed: 42);
        first.Schedule(5, Command.DebugSpawn(0, UnitCatalog.Footman, 10));
        second.Schedule(5, Command.DebugSpawn(0, UnitCatalog.Footman, 11));

        for (int tick = 0; tick < 20; tick++)
        {
            first.Step();
            second.Step();
        }
        Assert.NotEqual(first.Sim.ComputeStateHash(), second.Sim.ComputeStateHash());
    }

    [Fact]
    public void Placing_a_building_costs_gold_and_respects_the_build_zone()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        var zone = sim.Map.BuildZones(0)[0];
        int goldBefore = sim.Players[0].Gold;
        var enemyZone = sim.Map.BuildZones(1)[0];

        sim.Step([
            Command.PlaceBuilding(0, UnitCatalog.Barracks, zone.MinX + 4, zone.MinY + 4),
            Command.PlaceBuilding(0, UnitCatalog.Barracks, zone.MinX + 5, zone.MinY + 5), // overlaps the first one
            Command.PlaceBuilding(0, UnitCatalog.Barracks, enemyZone.MinX + 4, enemyZone.MinY + 4), // enemy zone
            Command.PlaceBuilding(0, UnitCatalog.Barracks, zone.MaxX - 1, zone.MinY + 8), // sticks out of the zone
        ]);

        int barracks = CountAlive(sim, UnitCatalog.Barracks);
        Assert.Equal(1, barracks);
        Assert.Equal(goldBefore - sim.Catalog[UnitCatalog.Barracks].Cost + 10, sim.Players[0].Gold);
    }

    [Fact]
    public void Buildings_spawn_units_that_walk_toward_the_enemy()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        var zone = sim.Map.BuildZones(0)[0];
        var (tileX, tileY) = Simulation.FootprintOrigin(zone.Center, 2);
        sim.Step([Command.PlaceBuilding(0, UnitCatalog.Barracks, tileX, tileY)]);

        for (int tick = 0; tick < Simulation.TicksPerSecond * 8; tick++)
            sim.Step([]);

        var u = sim.Units;
        // The first footman appears after 5 s, so it has walked for about 3 s (~6 tiles) by now.
        var footmen = Enumerable.Range(0, u.HighWater).Where(i => u.Alive[i] && u.Type[i] == UnitCatalog.Footman).ToList();
        Assert.NotEmpty(footmen);
        Assert.All(footmen, i => Assert.True(u.Pos[i].X > zone.Center.X + Fix.FromInt(5), $"footman only reached x={u.Pos[i].X}"));
    }

    [Fact]
    public void A_dense_crowd_keeps_flowing_instead_of_getting_stuck()
    {
        // A short map squeezes the staging area to 10x28 tiles: 400 footmen half fill it.
        var sim = new Simulation(GameSetup.ForTeams(1, 1), MapCatalog.CreateLaneMap("Short", laneCount: 1, laneWidth: 32, length: 120));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, 400)]);
        var u = sim.Units;
        var crowd = Enumerable.Range(0, u.HighWater).Where(i => u.Alive[i] && u.Type[i] == UnitCatalog.Footman).ToArray();
        var startX = crowd.ToDictionary(i => i, i => u.Pos[i].X);

        const int ticks = 200;
        for (int tick = 0; tick < ticks; tick++)
            sim.Step([]);

        // Nobody has an enemy in range yet (the castle is far away), so every unit should have
        // made real progress, even the ones that started buried in the middle of the blob.
        Fix fullDistance = sim.Catalog[UnitCatalog.Footman].Speed * ticks;
        Fix slowest = crowd.Select(i => u.Pos[i].X - startX[i]).Min();
        Assert.True(slowest > fullDistance / 2, $"slowest unit only moved {slowest} of {fullDistance} tiles");
    }

    [Fact]
    public void An_army_destroys_the_enemy_castle_and_wins()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, 150)]);

        for (int tick = 0; tick < Simulation.TicksPerSecond * 180 && !sim.IsGameOver; tick++)
            sim.Step([]);

        Assert.Equal(0, sim.WinningTeam);
    }

    [Fact]
    public void Hunters_prefer_their_favourite_target()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        sim.Step([
            Command.DebugSpawn(0, UnitCatalog.Hunter, 20),
            Command.DebugSpawn(1, UnitCatalog.Footman, 100),
            Command.DebugSpawn(1, UnitCatalog.Archer, 30),
        ]);

        int huntersOnArchers = 0, huntersWithTarget = 0;
        for (int tick = 0; tick < Simulation.TicksPerSecond * 4; tick++)
        {
            sim.Step([]);
            var u = sim.Units;
            for (int i = 0; i < u.HighWater; i++)
            {
                if (!u.Alive[i] || u.Type[i] != UnitCatalog.Hunter || !u.HasValidTarget(i))
                    continue;
                huntersWithTarget++;
                if (u.Type[u.Target[i]] == UnitCatalog.Archer)
                    huntersOnArchers++;
            }
        }

        Assert.True(huntersWithTarget > 0);
        Assert.True(huntersOnArchers * 2 > huntersWithTarget, $"{huntersOnArchers}/{huntersWithTarget} hunter targets were archers");
    }

    [Theory]
    [InlineData(MapId.SingleLane)]
    [InlineData(MapId.TwoLanes)]
    [InlineData(MapId.ThreeLanes)]
    public void Same_seed_gives_identical_state_on_every_map(MapId map)
    {
        var first = new Match(seed: 9, playersPerTeam: 3, map);
        var second = new Match(seed: 9, playersPerTeam: 3, map);
        first.Schedule(40, Command.DebugSpawn(0, UnitCatalog.Skirmisher, 200));
        second.Schedule(40, Command.DebugSpawn(0, UnitCatalog.Skirmisher, 200));

        for (int tick = 0; tick < 1500; tick++)
        {
            first.Step();
            second.Step();
            if (tick % 10 == 0)
                Assert.Equal(first.Sim.ComputeStateHash(), second.Sim.ComputeStateHash());
        }
        Assert.True(first.Sim.ValidateOccupancy());
    }

    [Fact]
    public void Different_maps_give_different_state_hashes()
    {
        var single = new Simulation(GameSetup.ForTeams(1, 1, MapId.SingleLane));
        var two = new Simulation(GameSetup.ForTeams(1, 1, MapId.TwoLanes));
        Assert.NotEqual(single.ComputeStateHash(), two.ComputeStateHash());
    }

    [Theory]
    [InlineData(MapId.SingleLane)]
    [InlineData(MapId.ThreeLanes)]
    public void Eight_players_is_the_maximum(MapId map)
    {
        var match = new Match(seed: 3, playersPerTeam: 4, map);
        for (int tick = 0; tick < Simulation.TicksPerSecond * 20; tick++)
            match.Step();

        Assert.Equal(8, match.Sim.Players.Count);
        Assert.All(match.Sim.Players, p => Assert.True(p.Gold < 300 + 10 * 20, $"player {p.Id} never built anything"));
        Assert.Throws<ArgumentException>(() => new Simulation(GameSetup.ForTeams(1, 5)));
    }

    [Fact]
    public void Freed_slots_are_reused_with_a_new_generation()
    {
        var pool = new UnitPool(initialCapacity: 2);
        int slot = pool.Allocate();
        int generation = pool.Generation[slot];
        pool.Free(slot);
        int reused = pool.Allocate();
        pool.Allocate();
        pool.Allocate(); // forces growth

        Assert.Equal(slot, reused);
        Assert.False(pool.IsAlive(slot, generation));
        Assert.True(pool.IsAlive(reused, pool.Generation[reused]));
    }

    static int CountAlive(Simulation sim, int typeId) =>
        Enumerable.Range(0, sim.Units.HighWater).Count(i => sim.Units.Alive[i] && sim.Units.Type[i] == typeId);

    /// <summary>A simulation driven the same way the game drives it: bots plus scheduled commands.</summary>
    internal sealed class Match
    {
        readonly CommandSchedule _schedule = new();
        readonly ScriptedBot[] _bots;

        public Match(ulong seed, int playersPerTeam = 3, MapId map = MapId.SingleLane)
        {
            var setup = GameSetup.ForTeams(seed, playersPerTeam, map);
            Sim = new Simulation(setup);
            _bots = Enumerable.Range(0, setup.PlayerTeams.Count).Select(id => new ScriptedBot(id, seed)).ToArray();
        }

        public Simulation Sim { get; }

        public void Schedule(int tick, Command command) => _schedule.Add(tick, command);

        public void Step()
        {
            foreach (var bot in _bots)
            {
                if (bot.TryThink(Sim, out var command))
                    _schedule.Add(Sim.Tick + InputDelay, command);
            }
            Sim.Step(_schedule.TakeForTick(Sim.Tick));
        }
    }
}
