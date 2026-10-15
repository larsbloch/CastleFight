using Godot;

namespace CastleFight.Game.UI;

/// <summary>A gold rule that fades out at both ends, with diamonds in the middle.</summary>
public partial class Ornament : Control
{
    public Color Color { get; set; } = UiTheme.Gold;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        float y = Size.Y / 2, w = Size.X, mid = w / 2;
        var clear = Color with { A = 0 };
        var dark = new Color(0.05f, 0.03f, 0.02f, 0.8f);
        var line = new Vector2[] { new(0, y), new(mid - 30, y), new(mid + 30, y), new(w, y) };
        DrawPolylineColors(line, new[] { dark with { A = 0 }, dark, dark, dark with { A = 0 } }, 5, true);
        DrawPolylineColors(line, new[] { clear, Color, Color, clear }, 2, true);

        Diamond(new Vector2(mid, y), 8, dark);
        Diamond(new Vector2(mid, y), 6, Color);
        Diamond(new Vector2(mid, y), 2.5f, dark);
        foreach (float side in new[] { -1f, 1f })
        {
            Diamond(new Vector2(mid + side * 20, y), 5, dark);
            Diamond(new Vector2(mid + side * 20, y), 3.5f, Color);
        }
    }

    void Diamond(Vector2 center, float r, Color color) =>
        DrawColoredPolygon(new[] { center + new Vector2(0, -r), center + new Vector2(r, 0), center + new Vector2(0, r), center + new Vector2(-r, 0) }, color);
}
