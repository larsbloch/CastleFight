using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class LaneTests
{
    [Fact]
    public void Units_take_the_lane_closest_to_where_their_building_stands()
    {
        // Three players on team 0 build at the top, middle and bottom of the zone.
        var sim = new Simulation(new GameSetup(1, [0, 0, 0, 1]) { Map = MapId.ThreeLanes });
        var zone = sim.Map.BuildZone(0);
        int middle = (zone.MinY + zone.MaxY) / 2 - 1;
        sim.Step([
            Command.PlaceBuilding(0, UnitCatalog.Barracks, zone.MaxX - 2, zone.MinY),
            Command.PlaceBuilding(1, UnitCatalog.Barracks, zone.MaxX - 2, middle),
            Command.PlaceBuilding(2, UnitCatalog.Barracks, zone.MaxX - 2, zone.MaxY - 2),
        ]);

        for (int tick = 0; tick < Simulation.TicksPerSecond * 30; tick++)
            sim.Step([]);

        var u = sim.Units;
        var inLanes = MobileUnits(sim).Where(i => u.Pos[i].X > Fix.FromInt(sim.Map.LaneMinX + 2)).ToList();
        for (int owner = 0; owner < 3; owner++)
        {
            var mine = inLanes.Where(i => u.Owner[i] == owner).ToList();
            Assert.NotEmpty(mine);
            Assert.All(mine, i => Assert.Equal(owner, sim.Map.NearestLane(u.Pos[i].Y).Index));
        }
    }

    [Fact]
    public void A_unit_pushed_elsewhere_heads_for_the_lane_closest_to_its_new_position()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1, MapId.ThreeLanes));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, 1)]);
        int unit = MobileUnits(sim).Single();
        var u = sim.Units;

        Teleport(sim, unit, new FixVec2(Fix.FromInt(30), Fix.FromInt(12)));
        Assert.Equal(0, sim.LaneOf(0, u.Pos[unit]));
        for (int tick = 0; tick < 40; tick++)
            sim.Step([]);

        // Something shoves it to the far side of the base: it re-routes to the bottom lane.
        Teleport(sim, unit, new FixVec2(Fix.FromInt(36), Fix.FromInt(96)));
        Assert.Equal(2, sim.LaneOf(0, u.Pos[unit]));
        for (int tick = 0; tick < Simulation.TicksPerSecond * 20; tick++)
            sim.Step([]);

        Assert.True(u.Pos[unit].X > Fix.FromInt(sim.Map.LaneMinX + 5));
        Assert.Equal(2, sim.Map.NearestLane(u.Pos[unit].Y).Index);
    }

    [Fact]
    public void The_flow_field_routes_around_buildings()
    {
        var sim = new Simulation(new GameSetup(1, [0, 1]) { StartingGold = 10_000 });
        var zone = sim.Map.BuildZone(0);
        int x = zone.MaxX - 2, y = zone.MinY + 10;
        sim.Step([Command.PlaceBuilding(0, UnitCatalog.Barracks, x, y)]);

        var field = sim.FlowField(0);
        var behind = new FixVec2(Fix.FromInt(x - 1) + Fix.Half, Fix.FromInt(y) + Fix.Half);
        Assert.NotEqual(sim.Map.Forward(0), field.DirectionAt(behind));
        Assert.True(field.DistanceAt(x - 1, y) > field.DistanceAt(x - 1, y - 3));
        Assert.Equal(-1, field.DistanceAt(x, y)); // inside the building
    }

    [Fact]
    public void Spreaders_fan_out_within_their_own_lane()
    {
        var sim = new Simulation(GameSetup.ForTeams(4, 1, MapId.ThreeLanes));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Skirmisher, 300)]);
        for (int tick = 0; tick < 300; tick++)
            sim.Step([]);

        var u = sim.Units;
        var inLanes = MobileUnits(sim).Where(i => u.Pos[i].X > Fix.FromInt(sim.Map.LaneMinX + 1)).ToList();
        Assert.NotEmpty(inLanes);
        foreach (int i in inLanes)
        {
            var lane = sim.Map.NearestLane(u.Pos[i].Y);
            Assert.InRange(u.Pos[i].Y.Raw, Fix.FromInt(lane.MinY).Raw, Fix.FromInt(lane.MaxY).Raw);
        }
        // They really do spread: every lane holds units near both of its edges.
        foreach (var lane in sim.Map.Lanes)
        {
            var ys = inLanes.Select(i => u.Pos[i].Y).Where(y => y >= Fix.FromInt(lane.MinY) && y <= Fix.FromInt(lane.MaxY)).ToList();
            Assert.NotEmpty(ys);
            Assert.True(ys.Max() - ys.Min() > Fix.FromInt(lane.MaxY - lane.MinY) / 2, $"lane {lane.Index} spread only {ys.Max() - ys.Min()}");
        }
    }

    [Fact]
    public void No_unit_ever_ends_up_inside_a_wall()
    {
        var match = new SimulationTests.Match(seed: 11, playersPerTeam: 3, MapId.ThreeLanes);
        match.Schedule(100, Command.DebugSpawn(0, UnitCatalog.Footman, 400));
        match.Schedule(100, Command.DebugSpawn(3, UnitCatalog.Skirmisher, 400));
        var sim = match.Sim;
        var u = sim.Units;
        Fix tolerance = Fix.FromRatio(1, 100);

        for (int tick = 0; tick < 3000; tick++)
        {
            match.Step();
            if (tick % 5 != 0)
                continue;
            foreach (int i in MobileUnits(sim))
                Assert.False(OverlapsWall(sim.Map, u.Pos[i], u.Radius[i] - tolerance), $"unit {u.Id[i]} at {u.Pos[i]} overlaps a wall at tick {sim.Tick}");
        }
    }

    [Theory]
    [InlineData(MapId.TwoLanes)]
    [InlineData(MapId.ThreeLanes)]
    public void A_big_crowd_flows_from_the_base_through_the_lanes(MapId mapId)
    {
        var sim = new Simulation(GameSetup.ForTeams(2, 1, mapId));
        const int count = 600;
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, count)]);
        var crowd = MobileUnits(sim).ToArray();
        var u = sim.Units;

        // Pack them into the base and mouth: 30 columns x 20 rows across the whole map width.
        Fix rowSpacing = (sim.Map.Width - Fix.FromInt(4)) / 20;
        for (int k = 0; k < crowd.Length; k++)
            Teleport(sim, crowd[k], new FixVec2(Fix.FromInt(24 + k % 30), Fix.FromInt(2) + rowSpacing * (k / 30)));

        Fix exitX = Fix.FromInt(sim.Map.LaneMaxX);
        var passed = new HashSet<int>();
        Fix speed = sim.Catalog[UnitCatalog.Footman].Speed;
        int unobstructedTicks = ((exitX - Fix.FromInt(24)) / speed).FloorToInt();
        int limit = unobstructedTicks * 13 / 10;
        int tick = 0;
        for (; tick < limit && passed.Count < count; tick++)
        {
            sim.Step([]);
            foreach (int i in crowd)
            {
                if (u.Alive[i] && u.Pos[i].X >= exitX)
                    passed.Add(i);
            }
        }

        Assert.True(passed.Count == count,
            $"{count - passed.Count} of {count} units did not get through the lanes within {limit} ticks " +
            $"(unobstructed walk {unobstructedTicks}); e.g. {string.Join(", ", crowd.Where(i => !passed.Contains(i)).Take(5).Select(i => u.Pos[i]))}");
    }

    [Fact]
    public void Units_do_not_notice_enemies_behind_a_wall()
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1, MapId.TwoLanes));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Hunter, 1), Command.DebugSpawn(1, UnitCatalog.Footman, 1)]);
        var u = sim.Units;
        int hunter = MobileUnits(sim).Single(i => u.Team[i] == 0);
        int footman = MobileUnits(sim).Single(i => u.Team[i] == 1);
        var wall = sim.Map.Walls[0];
        var hunterAt = new FixVec2(Fix.FromInt(sim.Map.LaneMinX - 2), Fix.FromInt(wall.MinY + 6));

        // In aggro range, but the straight line passes through the wall's corner.
        Teleport(sim, hunter, hunterAt);
        Teleport(sim, footman, new FixVec2(Fix.FromInt(sim.Map.LaneMinX + 2), Fix.FromInt(wall.MinY - 1)));
        u.RetargetTimer[hunter] = 1;
        sim.Step([]);
        Assert.False(u.HasValidTarget(hunter));

        // Same distance with a clear line of sight.
        Teleport(sim, hunter, hunterAt);
        Teleport(sim, footman, new FixVec2(Fix.FromInt(sim.Map.LaneMinX - 6), Fix.FromInt(wall.MinY - 1)));
        u.RetargetTimer[hunter] = 1;
        sim.Step([]);
        Assert.True(u.HasValidTarget(hunter));
        Assert.Equal(footman, u.Target[hunter]);
    }

    static bool OverlapsWall(MapDefinition map, FixVec2 position, Fix radius)
    {
        foreach (var tiles in map.Walls)
        {
            var wall = tiles.ToFixRect();
            var closest = new FixVec2(Fix.Clamp(position.X, wall.Min.X, wall.Max.X), Fix.Clamp(position.Y, wall.Min.Y, wall.Max.Y));
            if (FixVec2.DistanceSquared(position, closest) < radius * radius)
                return true;
        }
        return false;
    }

    static IEnumerable<int> MobileUnits(Simulation sim) =>
        Enumerable.Range(0, sim.Units.HighWater).Where(i => sim.Units.Alive[i] && !sim.Units.IsStructure[i]);

    static void Teleport(Simulation sim, int slot, FixVec2 position)
    {
        sim.Units.Pos[slot] = position;
        sim.Units.PrevPos[slot] = position;
    }
}
