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
        Assert.Equal(map.BuildZone(0).MirrorX(length), map.BuildZone(1));
        Assert.Equal(map.StagingArea(0).MirrorX(length), map.StagingArea(1));
        Assert.Equal(length - map.LaneMinX, map.LaneMaxX);
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void The_build_zone_reaches_every_lane(MapId id)
    {
        var map = MapCatalog.Get(id);
        var zone = map.BuildZone(0);
        Assert.All(map.Lanes, lane => Assert.True(zone.MinY < lane.MaxY && zone.MaxY > lane.MinY, $"lane {lane.Index} is out of reach"));
        Assert.True(zone.Width * zone.Height > 24 * 28, "the build zone must be bigger than the old 24x28 one");
    }

    [Fact]
    public void Maps_have_the_requested_number_of_lanes_and_walls_between_them()
    {
        Assert.Equal([1, 2, 3], MapCatalog.All.Select(id => MapCatalog.Get(id).Lanes.Count));
        Assert.Equal([0, 1, 2], MapCatalog.All.Select(id => MapCatalog.Get(id).Walls.Count));
        Assert.Equal(MapCatalog.All.Count, MapCatalog.All.Select(id => MapCatalog.Get(id).Fingerprint).Distinct().Count());
    }

    [Fact]
    public void Walls_are_thicker_than_any_aggro_reach()
    {
        // Units in neighbouring lanes should never notice each other through a wall.
        var catalog = UnitCatalog.CreateDefault();
        Fix reach = catalog.MobileTypeIds.Select(id => catalog[id].AggroRange + catalog[id].Radius * 2).Max();
        foreach (var id in MapCatalog.All)
            Assert.All(MapCatalog.Get(id).Walls, wall => Assert.True(Fix.FromInt(wall.Height) > reach));
    }

    [Fact]
    public void Resolving_walls_pushes_circles_out_and_leaves_lanes_alone()
    {
        var map = MapCatalog.Get(MapId.TwoLanes);
        var wall = map.Walls[0];
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
