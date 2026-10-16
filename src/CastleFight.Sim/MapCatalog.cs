namespace CastleFight.Sim;

/// <summary>Maps players can choose. The id is sent in <c>GameStart</c>, so never renumber existing entries.</summary>
public enum MapId : byte
{
    SingleLane = 0,
    TwoLanes = 1,
    ThreeLanes = 2,
}

public static class MapCatalog
{
    static readonly MapId[] AllIds = [MapId.SingleLane, MapId.TwoLanes, MapId.ThreeLanes];
    static readonly Lazy<MapDefinition>[] Maps =
    [
        new(() => CreateTerracedMap("Single Lane", laneCount: 1, laneWidth: 48, rampWidth: 16)),
        new(() => CreateTerracedMap("Two Lanes", laneCount: 2, laneWidth: 28, rampWidth: 12)),
        new(() => CreateTerracedMap("Three Lanes", laneCount: 3, laneWidth: 28, rampWidth: 12)),
    ];

    public static IReadOnlyList<MapId> All => AllIds;

    public static bool IsValid(MapId id) => (uint)id < (uint)Maps.Length;

    public static MapDefinition Get(MapId id) =>
        IsValid(id) ? Maps[(int)id].Value : throw new ArgumentOutOfRangeException(nameof(id), $"Unknown map {id}.");

    public static string DisplayName(MapId id) => Get(id).Name;

    /// <summary>
    /// Castle Fight style map. Each base climbs in two steps from the lanes: a middle terrace (level 1) and the
    /// castle terrace (level 2), separated by thin cliff walls. Every cliff has one ramp per lane, in line with
    /// the lane's centre, and both terraces hold a build zone. Team 0's side is built here and mirrored for team 1.
    /// </summary>
    /// <param name="rampWidth">Width of every gap in the cliffs.</param>
    /// <param name="castleTerraceEnd">Where the castle terrace ends and the first cliff starts.</param>
    /// <param name="middleTerraceEnd">Where the middle terrace ends; the lanes start one cliff further on.</param>
    public static MapDefinition CreateTerracedMap(string name, int laneCount, int laneWidth, int rampWidth, int wallWidth = 12,
        int length = 240, int castleTerraceEnd = 36, int middleTerraceEnd = 66, int cliffThickness = 2, int rampRun = 4, int zoneInsetY = 2)
    {
        int width = laneCount * laneWidth + (laneCount - 1) * wallWidth;
        var lanes = LaneBands(laneCount, laneWidth, wallWidth);
        int laneMinX = middleTerraceEnd + cliffThickness;
        var gaps = lanes.Select(l => (MinY: (l.MinY + l.MaxY - rampWidth) / 2, MaxY: (l.MinY + l.MaxY + rampWidth) / 2)).ToArray();

        var upper = new Cliff(castleTerraceEnd, castleTerraceEnd + cliffThickness, UpperLevel: 2, UpperSideIsMinX: true, gaps, rampRun);
        var lower = new Cliff(middleTerraceEnd, laneMinX, UpperLevel: 1, UpperSideIsMinX: true, gaps, rampRun);

        // Each zone stops short of the cliffs and the map edges, so a path around any row of buildings always remains.
        var castle = new TileRect(4, width / 2 - 2, 8, width / 2 + 2);
        var castleZone = new TileRect(castle.MaxX + 3, zoneInsetY, castleTerraceEnd - 3, width - zoneInsetY);
        var middleZone = new TileRect(upper.MaxX + 1, zoneInsetY, middleTerraceEnd - 2, width - zoneInsetY);
        var staging = new TileRect(middleZone.MinX, 2, length / 2 - 4, width - 2);

        TileRect[] zones = [castleZone, middleZone];
        return new MapDefinition(name, length, width, laneMinX, length - laneMinX, lanes,
            [castle, castle.MirrorX(length)], [zones, zones.Select(z => z.MirrorX(length)).ToArray()], [staging, staging.MirrorX(length)],
            [upper, lower, lower.MirrorX(length), upper.MirrorX(length)]);
    }

    /// <summary>
    /// Flat map without cliffs: open bases at both ends and <paramref name="laneCount"/> parallel lanes separated
    /// by walls, with one build zone per team spanning every lane's band (tests and benchmarks).
    /// </summary>
    /// <param name="length">Map length in tiles (castle to castle is length - 12).</param>
    /// <param name="laneMinX">Where the lanes start; the 12 tiles between the build zone and this are an open mouth.</param>
    public static MapDefinition CreateLaneMap(string name, int laneCount, int laneWidth, int wallWidth = 12,
        int length = 224, int laneMinX = 56, int zoneMinX = 12, int zoneDepth = 32, int zoneInsetY = 4)
    {
        int width = laneCount * laneWidth + (laneCount - 1) * wallWidth;
        var lanes = LaneBands(laneCount, laneWidth, wallWidth);

        var castle = new TileRect(4, width / 2 - 2, 8, width / 2 + 2);
        var zone = new TileRect(zoneMinX, zoneInsetY, zoneMinX + zoneDepth, width - zoneInsetY);
        var staging = new TileRect(zone.MaxX + 2, 2, length / 2 - 4, width - 2);

        return new MapDefinition(name, length, width, laneMinX, length - laneMinX, lanes,
            [castle, castle.MirrorX(length)], [[zone], [zone.MirrorX(length)]], [staging, staging.MirrorX(length)]);
    }

    static (int MinY, int MaxY)[] LaneBands(int laneCount, int laneWidth, int wallWidth) =>
        Enumerable.Range(0, laneCount)
            .Select(i => (MinY: i * (laneWidth + wallWidth), MaxY: i * (laneWidth + wallWidth) + laneWidth))
            .ToArray();
}
