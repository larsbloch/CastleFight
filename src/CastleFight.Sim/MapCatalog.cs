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
        new(() => CreateLaneMap("Single Lane", laneCount: 1, laneWidth: 48, zoneInsetY: 4)),
        new(() => CreateLaneMap("Two Lanes", laneCount: 2, laneWidth: 28, zoneInsetY: 6)),
        new(() => CreateLaneMap("Three Lanes", laneCount: 3, laneWidth: 28, zoneInsetY: 8)),
    ];

    public static IReadOnlyList<MapId> All => AllIds;

    public static bool IsValid(MapId id) => (uint)id < (uint)Maps.Length;

    public static MapDefinition Get(MapId id) =>
        IsValid(id) ? Maps[(int)id].Value : throw new ArgumentOutOfRangeException(nameof(id), $"Unknown map {id}.");

    public static string DisplayName(MapId id) => Get(id).Name;

    /// <summary>
    /// Symmetric map: open bases at both ends and <paramref name="laneCount"/> parallel lanes separated by
    /// walls. Team 0's side is built here and mirrored for team 1. The build zone spans every lane's band, so
    /// where a building stands decides which lane its units take.
    /// </summary>
    /// <param name="length">Map length in tiles (castle to castle is length - 12).</param>
    /// <param name="laneMinX">Where the lanes start; the 12 tiles between the build zone and this are an open mouth.</param>
    public static MapDefinition CreateLaneMap(string name, int laneCount, int laneWidth, int wallWidth = 12,
        int length = 224, int laneMinX = 56, int zoneMinX = 12, int zoneDepth = 32, int zoneInsetY = 4)
    {
        int width = laneCount * laneWidth + (laneCount - 1) * wallWidth;
        var lanes = Enumerable.Range(0, laneCount)
            .Select(i => (MinY: i * (laneWidth + wallWidth), MaxY: i * (laneWidth + wallWidth) + laneWidth))
            .ToArray();

        var castle = new TileRect(4, width / 2 - 2, 8, width / 2 + 2);
        var zone = new TileRect(zoneMinX, zoneInsetY, zoneMinX + zoneDepth, width - zoneInsetY);
        var staging = new TileRect(zone.MaxX + 2, 2, length / 2 - 4, width - 2);

        return new MapDefinition(name, length, width, laneMinX, length - laneMinX, lanes,
            [castle, castle.MirrorX(length)], [zone, zone.MirrorX(length)], [staging, staging.MirrorX(length)]);
    }
}
