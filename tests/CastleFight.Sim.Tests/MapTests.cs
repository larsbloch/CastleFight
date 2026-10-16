using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class MapTests
{
    public static TheoryData<MapId> AllMaps => new(MapCatalog.All);

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Catalog_maps_are_mirror_symmetric(MapId id)
    {
        var map = MapCatalog.Get(id);
        int length = map.LengthTiles;

        Assert.Equal(map.CastleFootprint(0).MirrorX(length), map.CastleFootprint(1));
        Assert.Equal(map.BuildZones(0).Select(z => z.MirrorX(length)), map.BuildZones(1));
        Assert.Equal(map.StagingArea(0).MirrorX(length), map.StagingArea(1));
        Assert.Equal(length - map.LaneMinX, map.LaneMaxX);
        Assert.Equal(map.Cliffs.Select(c => c.MirrorX(length)).Reverse(), map.Cliffs);
        Assert.Equal(map.CliffWalls.Select(w => w.MirrorX(length)).OrderBy(w => (w.MinX, w.MinY)), map.CliffWalls.OrderBy(w => (w.MinX, w.MinY)));
        Assert.Equal(map.Terraces.Select(t => t with { Area = t.Area.MirrorX(length) }).Reverse(), map.Terraces);
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Every_build_zone_reaches_every_lane(MapId id)
    {
        var map = MapCatalog.Get(id);
        Assert.Equal(2, map.BuildZones(0).Count);
        foreach (var zone in map.BuildZones(0))
            Assert.All(map.Lanes, lane => Assert.True(zone.MinY < lane.MaxY && zone.MaxY > lane.MinY, $"lane {lane.Index} is out of reach"));
        Assert.True(map.BuildZones(0).Sum(z => z.Width * z.Height) > 32 * (map.WidthTiles - 8), "the zones must be bigger than the old single zone");
    }

    [Fact]
    public void Maps_have_the_requested_number_of_lanes_and_walls_between_them()
    {
        Assert.Equal([1, 2, 3], MapCatalog.All.Select(id => MapCatalog.Get(id).Lanes.Count));
        Assert.Equal([0, 1, 2], MapCatalog.All.Select(id => MapCatalog.Get(id).LaneWalls.Count));
        Assert.Equal(MapCatalog.All.Count, MapCatalog.All.Select(id => MapCatalog.Get(id).Fingerprint).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Each_base_climbs_two_cliffs_with_one_ramp_per_lane(MapId id)
    {
        var map = MapCatalog.Get(id);
        Assert.Equal(4, map.Cliffs.Count);
        Assert.Equal([2, 1, 0, 1, 2], map.Terraces.Select(t => t.Level));
        Assert.Equal(map.Lanes.Count * 4, map.Ramps.Count);
        Assert.Equal(2, map.TerraceAt(map.CastleFootprint(0).MinX)!.Level);
        Assert.Equal(0, map.TerraceAt(map.LaneMinX)!.Level);

        foreach (var cliff in map.Cliffs)
        {
            // Every gap sits in line with a lane's centre.
            Assert.Equal(map.Lanes.Count, cliff.Gaps.Count);
            for (int i = 0; i < map.Lanes.Count; i++)
            {
                var lane = map.Lanes[i];
                Assert.Equal(lane.MinY + lane.MaxY, cliff.Gaps[i].MinY + cliff.Gaps[i].MaxY);
            }
        }
        foreach (var zone in map.BuildZones(0))
            Assert.NotNull(map.TerraceAt(zone.MinX));
    }

    [Fact]
    public void Lane_walls_are_thicker_than_any_aggro_reach()
    {
        // Units in neighbouring lanes should never notice each other through a wall.
        var catalog = UnitCatalog.CreateDefault();
        Fix reach = catalog.MobileTypeIds.Select(id => catalog[id].AggroRange + catalog[id].Radius * 2).Max();
        foreach (var id in MapCatalog.All)
            Assert.All(MapCatalog.Get(id).LaneWalls, wall => Assert.True(Fix.FromInt(wall.Height) > reach));
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Cliffs_can_only_be_crossed_on_the_ramps(MapId id)
    {
        var map = MapCatalog.Get(id);
        foreach (var cliff in map.Cliffs)
        {
            for (int y = 0; y < map.WidthTiles; y++)
            {
                bool inGap = cliff.Gaps.Any(g => y >= g.MinY && y < g.MaxY);
                for (int x = cliff.MinX; x < cliff.MaxX; x++)
                    Assert.Equal(!inGap, map.IsWallTile(x, y));
            }
        }
        Assert.All(map.Ramps, ramp => Assert.DoesNotContain(map.Walls, ramp.Slope.Overlaps));
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Every_open_tile_of_both_bases_leads_to_the_lanes_and_to_the_enemy_castle(MapId id)
    {
        var sim = new Simulation(GameSetup.ForTeams(1, 1, id));
        var map = sim.Map;
        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var own = map.BaseArea(team);
            var enemy = map.BaseArea(1 - team);
            for (int y = 0; y < map.WidthTiles; y++)
            {
                for (int x = own.MinX; x < own.MaxX; x++)
                {
                    if (!map.IsWallTile(x, y) && sim.TileOccupant(x, y) < 0)
                        Assert.True(sim.FlowField(team).DistanceAt(x, y) >= 0, $"team {team} is stuck at ({x}, {y}) in its own base");
                }
                for (int x = enemy.MinX; x < enemy.MaxX; x++)
                {
                    if (!map.IsWallTile(x, y))
                        Assert.True(sim.CastleField(team).DistanceAt(x, y) >= 0, $"team {team} cannot reach the enemy castle from ({x}, {y})");
                }
            }
        }
    }

    [Fact]
    public void Resolving_walls_pushes_circles_out_and_leaves_lanes_alone()
    {
        var map = MapCatalog.Get(MapId.TwoLanes);
        var wall = map.LaneWalls[0];
        Fix radius = Fix.FromRatio(4, 10);

        var inLane = new FixVec2(Fix.FromInt(100), Fix.FromInt(10));
        Assert.Equal(inLane, map.ResolveWalls(inLane, radius));

        var touching = new FixVec2(Fix.FromInt(100), Fix.FromInt(wall.MinY) - Fix.FromRatio(1, 10));
        var pushed = map.ResolveWalls(touching, radius);
        Assert.True(Fix.Abs(Fix.FromInt(wall.MinY) - radius - pushed.Y) < Fix.FromRatio(1, 1000), $"pushed to {pushed.Y}");

        var inside = new FixVec2(Fix.FromInt(100), Fix.FromInt(wall.MinY + 2));
        Assert.False(map.IsInsideWall(map.ResolveWalls(inside, radius), Fix.Zero));
    }

    [Fact]
    public void Line_of_sight_is_blocked_by_walls()
    {
        var map = MapCatalog.Get(MapId.TwoLanes);
        var top = new FixVec2(Fix.FromInt(100), Fix.FromInt(20));
        var bottom = new FixVec2(Fix.FromInt(100), Fix.FromInt(50));
        var alsoTop = new FixVec2(Fix.FromInt(110), Fix.FromInt(5));
        var inBase = new FixVec2(Fix.FromInt(40), Fix.FromInt(34));

        Assert.True(map.SegmentCrossesWall(top, bottom));
        Assert.False(map.SegmentCrossesWall(top, alsoTop));
        Assert.False(map.SegmentCrossesWall(inBase, new FixVec2(Fix.FromInt(50), Fix.FromInt(10))));
    }
}
