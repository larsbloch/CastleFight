namespace CastleFight.Sim.Tests;

public class PlacementTests
{
    static Simulation RichSim(MapId map = MapId.SingleLane) =>
        new(new GameSetup(1, [0, 1]) { StartingGold = 100_000, Map = map });

    [Fact]
    public void Buildings_can_stand_side_by_side_but_not_overlap()
    {
        var sim = RichSim();
        var zone = sim.Map.BuildZones(0)[0];
        int x = zone.MinX, y = zone.MinY;

        sim.Step([
            Command.PlaceBuilding(0, UnitCatalog.Barracks, x, y),
            Command.PlaceBuilding(0, UnitCatalog.Kennel, x + 2, y), // touching on the right
            Command.PlaceBuilding(0, UnitCatalog.Kennel, x, y + 2), // touching above
            Command.PlaceBuilding(0, UnitCatalog.Kennel, x + 1, y + 1), // overlaps all three
        ]);

        Assert.Equal(3, CountStructures(sim, owner: 0));
        Assert.Equal(sim.TileOccupant(x, y), sim.TileOccupant(x + 1, y + 1));
        Assert.NotEqual(-1, sim.TileOccupant(x + 3, y + 1));
        Assert.True(sim.ValidateOccupancy());
    }

    [Fact]
    public void Placement_outside_the_own_zone_or_off_the_map_is_rejected()
    {
        var sim = RichSim();
        var zone = sim.Map.BuildZones(0)[0];
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MinX - 1, zone.MinY));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MaxX - 1, zone.MinY));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MinX, zone.MaxY - 1));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, short.MinValue, short.MaxValue));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, sim.Map.BuildZones(1)[0].MinX, sim.Map.BuildZones(1)[0].MinY));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Footman, zone.MinX, zone.MinY));
        Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Castle, zone.MinX, zone.MinY));
        Assert.True(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MaxX - 2, zone.MaxY - 2));
    }

    [Theory]
    [InlineData(MapId.SingleLane)]
    [InlineData(MapId.TwoLanes)]
    [InlineData(MapId.ThreeLanes)]
    public void Both_terraces_are_buildable_but_ramps_are_not(MapId map)
    {
        var sim = RichSim(map);
        foreach (var zone in sim.Map.BuildZones(0))
            Assert.True(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MinX, zone.MinY));

        // The middle terrace's zone runs over the landing at the foot of the upper ramps.
        var middle = sim.Map.BuildZones(0)[1];
        var ramp = sim.Map.Ramps.First(r => r.KeepClear.Overlaps(middle));
        Assert.Equal(PlacementResult.OnRamp, sim.CheckPlacement(0, UnitCatalog.Barracks, middle.MinX, ramp.Slope.MinY));
        Assert.Equal(PlacementResult.OnRamp, sim.CheckPlacement(0, UnitCatalog.Barracks, middle.MinX, ramp.Slope.MaxY - 1));
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, UnitCatalog.Barracks, middle.MinX, ramp.Slope.MaxY));
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, UnitCatalog.Barracks, ramp.KeepClear.MaxX, ramp.Slope.MinY));

        // No zone reaches onto a cliff, and no tile of any ramp is buildable.
        foreach (var r in sim.Map.Ramps)
        {
            for (int y = r.KeepClear.MinY - 1; y < r.KeepClear.MaxY; y++)
            {
                for (int x = r.KeepClear.MinX - 1; x < r.KeepClear.MaxX; x++)
                    Assert.False(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, x, y), $"built on a ramp at ({x}, {y})");
            }
        }
    }

    [Fact]
    public void Castles_occupy_their_footprint()
    {
        var sim = RichSim();
        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var castle = sim.Map.CastleFootprint(team);
            int slot = sim.TileOccupant(castle.MinX, castle.MinY);
            Assert.True(slot >= 0 && sim.Units.Type[slot] == UnitCatalog.Castle);
            Assert.Equal(slot, sim.TileOccupant(castle.MaxX - 1, castle.MaxY - 1));
            Assert.Equal(-1, sim.TileOccupant(castle.MaxX, castle.MinY));
        }
    }

    [Fact]
    public void A_destroyed_building_frees_its_tiles()
    {
        var sim = RichSim();
        var zone = sim.Map.BuildZones(0)[0];
        sim.Step([Command.PlaceBuilding(0, UnitCatalog.Barracks, zone.MinX, zone.MinY)]);
        int slot = sim.TileOccupant(zone.MinX, zone.MinY);
        Assert.True(slot >= 0);

        sim.Units.Hp[slot] = 0;
        sim.Step([]);

        Assert.Equal(-1, sim.TileOccupant(zone.MinX, zone.MinY));
        Assert.True(sim.CanPlaceBuilding(0, UnitCatalog.Barracks, zone.MinX, zone.MinY));
        Assert.True(sim.ValidateOccupancy());
    }

    [Fact]
    public void A_blocked_front_makes_units_spawn_in_a_free_gap()
    {
        var sim = RichSim();
        var zone = sim.Map.BuildZones(0)[0];
        int x = zone.MinX + 10, y = zone.MinY + 10;
        // Barracks with a solid wall of kennels right in front of it (and over its front corners).
        var commands = new List<Command> { Command.PlaceBuilding(0, UnitCatalog.Barracks, x, y) };
        for (int dy = -2; dy <= 2; dy += 2)
            commands.Add(Command.PlaceBuilding(0, UnitCatalog.Kennel, x + 2, y + dy));
        sim.Step(commands.ToArray());

        for (int tick = 0; tick < sim.Catalog[UnitCatalog.Barracks].SpawnIntervalTicks + 1; tick++)
            sim.Step([]);

        var u = sim.Units;
        int footman = Enumerable.Range(0, u.HighWater).Single(i => u.Alive[i] && u.Type[i] == UnitCatalog.Footman);
        Assert.Equal(-1, sim.TileOccupant(u.Pos[footman].X.FloorToInt(), u.Pos[footman].Y.FloorToInt()));
    }

    [Fact]
    public void Bots_keep_occupancy_consistent_over_a_long_match()
    {
        var match = new SimulationTests.Match(seed: 5, playersPerTeam: 4, MapId.ThreeLanes);
        for (int tick = 0; tick < 4000; tick++)
            match.Step();

        Assert.True(CountStructures(match.Sim, owner: 0) > 3);
        Assert.True(match.Sim.ValidateOccupancy());
    }

    static int CountStructures(Simulation sim, int owner) =>
        Enumerable.Range(0, sim.Units.HighWater).Count(i => sim.Units.Alive[i] && sim.Units.IsStructure[i] && sim.Units.Owner[i] == owner);
}
