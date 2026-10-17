using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class EndgameTests
{
    [Fact]
    public void When_a_castle_falls_production_income_fighting_and_building_stop()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        var zone = sim.Map.BuildZones(0)[0];
        var (tileX, tileY) = Simulation.FootprintOrigin(zone.Center, 2);
        sim.Step([
            Command.PlaceBuilding(0, UnitCatalog.Barracks, tileX, tileY),
            Command.DebugSpawn(0, UnitCatalog.Footman, 20),
            Command.DebugSpawn(1, UnitCatalog.Footman, 20),
        ]);
        var enemyZone = sim.Map.BuildZones(1)[0];
        sim.Step([
            Command.Build(1, UnitCatalog.Barracks, enemyZone.MinX + 2, enemyZone.MinY + 2),
            Command.Build(1, UnitCatalog.Barracks, enemyZone.MinX + 6, enemyZone.MinY + 2, queued: true),
        ]);
        Assert.NotEmpty(sim.Players[1].Builder.Queue);

        KillCastle(sim, team: 1);
        Assert.Equal(0, sim.WinningTeam);
        Assert.Equal(sim.Tick - 1, sim.GameOverTick);
        Assert.Equal(sim.Map.CastlePosition(1), sim.HolePosition);
        Assert.All(sim.Players, p => Assert.Equal(BuilderOrder.Idle, p.Builder.Order));
        Assert.All(sim.Players, p => Assert.Empty(p.Builder.Queue));

        int units = sim.Units.AliveCount;
        var gold = sim.Players.Select(p => p.Gold).ToArray();
        var builderPos = sim.Players[0].Builder.Pos;
        var hpBefore = sim.Units.Hp.Take(sim.Units.HighWater).ToArray();

        // The barracks would spawn a footman within these 5 s; nothing may spawn, die, earn or build.
        for (int tick = 0; tick < Simulation.TeardownTicks - 1; tick++)
            sim.Step([Command.MoveBuilder(0, builderPos + new FixVec2(Fix.FromInt(10), Fix.Zero))]);

        Assert.False(sim.IsHoleOpen);
        Assert.Equal(units, sim.Units.AliveCount);
        Assert.Equal(gold, sim.Players.Select(p => p.Gold));
        Assert.Equal(builderPos, sim.Players[0].Builder.Pos);
        Assert.Equal(hpBefore, sim.Units.Hp.Take(sim.Units.HighWater));
    }

    [Fact]
    public void After_the_teardown_every_unit_walks_into_the_hole_and_is_counted()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        var zone = sim.Map.BuildZones(1)[0];
        sim.Step([
            Command.PlaceBuilding(1, UnitCatalog.Barracks, zone.MinX + 6, zone.MinY + 6), // something to walk around
            Command.DebugSpawn(0, UnitCatalog.Footman, 60),
            Command.DebugSpawn(0, UnitCatalog.Archer, 40),
            Command.DebugSpawn(1, UnitCatalog.Skirmisher, 60),
        ]);
        KillCastle(sim, team: 1);
        int mobile = CountMobile(sim);
        int structures = sim.Units.AliveCount - mobile;
        Assert.True(mobile > 100);

        while (sim.Tick - sim.GameOverTick <= Simulation.TeardownTicks)
        {
            Assert.False(sim.IsHoleOpen);
            sim.Step([]);
        }
        Assert.True(sim.IsHoleOpen);

        for (int tick = 0; tick < Simulation.TicksPerSecond * 300 && CountMobile(sim) > 0; tick++)
            sim.Step([]);

        Assert.Equal(0, CountMobile(sim));
        Assert.Equal(mobile, sim.HoleDrops);
        Assert.Equal(structures, sim.Units.AliveCount); // buildings stay where they are
    }

    [Fact]
    public void Units_spawned_after_the_hole_opened_drop_in_too()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1));
        KillCastle(sim, team: 0);
        for (int tick = 0; tick <= Simulation.TeardownTicks; tick++)
            sim.Step([]);
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, 30)]);

        for (int tick = 0; tick < Simulation.TicksPerSecond * 120 && CountMobile(sim) > 0; tick++)
            sim.Step([]);
        Assert.Equal(30, sim.HoleDrops);
    }

    static void KillCastle(Simulation sim, int team)
    {
        var u = sim.Units;
        int castle = Enumerable.Range(0, u.HighWater).Single(i => u.Alive[i] && u.Type[i] == sim.Catalog.CastleTypeId && u.Team[i] == team);
        u.Hp[castle] = 0;
        sim.Step([]);
    }

    static int CountMobile(Simulation sim)
    {
        var u = sim.Units;
        return Enumerable.Range(0, u.HighWater).Count(i => u.Alive[i] && !u.IsStructure[i]);
    }
}
