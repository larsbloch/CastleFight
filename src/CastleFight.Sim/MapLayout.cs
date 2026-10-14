using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// A single straight lane. Team 0 starts on the left (low X) and marches right, team 1 the opposite.
/// Behind each army is its castle, then a build zone, then a staging area used for debug spawns.
/// </summary>
public sealed class MapLayout
{
    readonly FixVec2[] _castles;
    readonly FixRect[] _buildZones;
    readonly FixRect[] _stagingAreas;
    readonly FixVec2[] _laneDirections;

    public MapLayout(int length, int width)
    {
        if (length < 100 || width < 8)
            throw new ArgumentOutOfRangeException(nameof(length), "The lane must be at least 100x8 tiles.");

        Length = Fix.FromInt(length);
        Width = Fix.FromInt(width);
        Fix middle = Fix.FromInt(width) / 2;
        Fix edge = Fix.FromInt(2);

        _castles = [new FixVec2(Fix.FromInt(6), middle), new FixVec2(Fix.FromInt(length - 6), middle)];
        _buildZones =
        [
            new FixRect(new FixVec2(Fix.FromInt(12), edge), new FixVec2(Fix.FromInt(36), Width - edge)),
            new FixRect(new FixVec2(Fix.FromInt(length - 36), edge), new FixVec2(Fix.FromInt(length - 12), Width - edge)),
        ];
        _stagingAreas =
        [
            new FixRect(new FixVec2(Fix.FromInt(38), edge), new FixVec2(Fix.FromInt(length / 2 - 4), Width - edge)),
            new FixRect(new FixVec2(Fix.FromInt(length / 2 + 4), edge), new FixVec2(Fix.FromInt(length - 38), Width - edge)),
        ];
        _laneDirections = [new FixVec2(Fix.One, Fix.Zero), new FixVec2(-Fix.One, Fix.Zero)];
    }

    public static MapLayout CreateDefault() => new(160, 32);

    public Fix Length { get; }
    public Fix Width { get; }

    public FixVec2 CastlePosition(int team) => _castles[team];
    public FixRect BuildZone(int team) => _buildZones[team];
    public FixRect StagingArea(int team) => _stagingAreas[team];
    public FixVec2 LaneDirection(int team) => _laneDirections[team];
}
