using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// A small picture of a map drawn from its real geometry, squeezed to the icon's box: grass, forest walls
/// between the lanes, dirt paths, both bases with their build zones and a castle at each end.
/// </summary>
public partial class MapIcon : Control
{
    static readonly Color Grass = new(0.2f, 0.24f, 0.12f);
    static readonly Color Forest = new(0.07f, 0.11f, 0.06f);
    static readonly Color Dirt = new(0.5f, 0.39f, 0.23f);
    static readonly Color BaseGround = new(0.3f, 0.26f, 0.16f);

    MapId _map;

    public MapId Map
    {
        get => _map;
        set
        {
            _map = value;
            QueueRedraw();
        }
    }

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var map = MapCatalog.Get(_map);
        var area = new Rect2(Vector2.Zero, Size).Grow(-2);
        var scale = new Vector2(area.Size.X / map.LengthTiles, area.Size.Y / map.WidthTiles);
        Rect2 ToIcon(TileRect t) => new(area.Position + new Vector2(t.MinX, t.MinY) * scale, new Vector2(t.Width, t.Height) * scale);

        DrawRect(area, Grass);

        // Forest between the lanes, with a scatter of darker tree tops.
        var random = new RandomNumberGenerator { Seed = (ulong)_map + 1 };
        foreach (var wall in map.Walls)
        {
            var rect = ToIcon(wall);
            DrawRect(rect, Forest);
            int trees = (int)(rect.Size.X * rect.Size.Y / 18);
            for (int i = 0; i < trees; i++)
            {
                var at = rect.Position + new Vector2(random.Randf() * rect.Size.X, random.Randf() * rect.Size.Y);
                DrawCircle(at, 1.2f + random.Randf() * 1.6f, new Color(0.04f, 0.07f, 0.035f));
            }
        }

        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var baseRect = ToIcon(map.BaseArea(team));
            DrawRect(baseRect, BaseGround);
            DrawRect(baseRect, UnitRenderer.TeamColors[team] with { A = 0.13f });
            DrawRect(ToIcon(map.BuildZone(team)), UiTheme.TeamText[team] with { A = 0.45f }, filled: false, width: 1);
        }

        // Paths: narrower than the lane so the grass shows on both sides, running into both bases.
        foreach (var lane in map.Lanes)
        {
            var corridor = ToIcon(new TileRect(map.LaneMinX - 6, lane.MinY, map.LaneMaxX + 6, lane.MaxY));
            float inset = corridor.Size.Y * (map.Lanes.Count == 1 ? 0.3f : 0.2f);
            var path = corridor.GrowSide(Side.Top, -inset).GrowSide(Side.Bottom, -inset);
            DrawRect(path.Grow(1), new Color(0.24f, 0.18f, 0.1f));
            DrawRect(path, Dirt);
            float y = path.GetCenter().Y;
            for (float x = path.Position.X + 4; x < path.End.X - 6; x += 9)
                DrawLine(new Vector2(x, y), new Vector2(x + 4, y), new Color(0.72f, 0.6f, 0.4f, 0.55f), 1);
        }

        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var c = map.CastlePosition(team);
            DrawCastle(area.Position + new Vector2(c.X.ToFloat(), c.Y.ToFloat()) * scale, Mathf.Clamp(area.Size.Y * 0.16f, 9, 15), team);
        }

        DrawRect(area.Grow(1), new Color(0.03f, 0.02f, 0.01f), filled: false, width: 2);
    }

    void DrawCastle(Vector2 center, float size, int team)
    {
        var color = UiTheme.TeamText[team];
        var outline = new Color(0.03f, 0.02f, 0.01f);
        var body = new Rect2(center - new Vector2(size / 2, size * 0.3f), new Vector2(size, size * 0.75f));
        float merlon = size / 5;
        DrawRect(body.Grow(1.5f), outline);
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(body.Position.X + i * 2 * merlon - 1.5f, body.Position.Y - merlon - 1.5f, merlon + 3, merlon + 1.5f), outline);
        DrawRect(body, color);
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(body.Position.X + i * 2 * merlon, body.Position.Y - merlon, merlon, merlon), color);
        DrawRect(new Rect2(center.X - merlon / 2, body.End.Y - merlon * 1.6f, merlon, merlon * 1.6f), outline); // gate
    }
}
