using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Ground height for drawing: terraces are flat, ramps slope from one level to the next. Height is only for
/// looks; the simulation stays flat. Looked up per tile, so it is cheap enough for every unit every frame.
/// </summary>
public sealed class TerrainHeights
{
    /// <summary>World height of one terrace level.</summary>
    public const float LevelHeight = 2f;

    readonly MapDefinition _map;
    readonly Ramp[] _ramps;

    // Per tile: >= 0 is a flat level, < 0 is -(ramp index + 1).
    readonly short[] _tiles;

    public TerrainHeights(MapDefinition map)
    {
        _map = map;
        _ramps = [.. map.Ramps];
        _tiles = new short[map.LengthTiles * map.WidthTiles];
        for (int y = 0; y < map.WidthTiles; y++)
        {
            for (int x = 0; x < map.LengthTiles; x++)
                _tiles[y * map.LengthTiles + x] = Classify(x, y);
        }
    }

    /// <summary>Ground height at world (x, z), i.e. sim (x, y).</summary>
    public float At(float x, float z)
    {
        int tileX = Mathf.Clamp(Mathf.FloorToInt(x), 0, _map.LengthTiles - 1);
        int tileY = Mathf.Clamp(Mathf.FloorToInt(z), 0, _map.WidthTiles - 1);
        int code = _tiles[tileY * _map.LengthTiles + tileX];
        if (code >= 0)
            return code * LevelHeight;
        return RampHeight(_ramps[-code - 1], x);
    }

    /// <summary>Height of a ramp's surface at world x (clamped to the ramp).</summary>
    public static float RampHeight(Ramp ramp, float x)
    {
        float t = Mathf.Clamp((x - ramp.Slope.MinX) / ramp.Slope.Width, 0f, 1f);
        float level = ramp.HighAtMinX ? ramp.HighLevel - t : ramp.LowLevel + t;
        return level * LevelHeight;
    }

    public static float LevelToHeight(int level) => level * LevelHeight;

    short Classify(int x, int y)
    {
        for (int i = 0; i < _ramps.Length; i++)
        {
            if (_ramps[i].Slope.Contains(x, y))
                return (short)-(i + 1);
        }
        if (_map.TerraceAt(x) is { } terrace)
            return (short)terrace.Level;
        // A cliff column outside the gaps: the top of the cliff.
        foreach (var cliff in _map.Cliffs)
        {
            if (x >= cliff.MinX && x < cliff.MaxX)
                return (short)cliff.UpperLevel;
        }
        return 0;
    }
}
