using Godot;

namespace CastleFight.Game.UI;

public enum ResourceKind { Gold, Wood, Food }

/// <summary>A small resource picture for the top bar (a gold coin, a pine tree, a drumstick), drawn as shapes.</summary>
public partial class ResourceIcon : Control
{
    static readonly Color Outline = new(0.05f, 0.03f, 0.02f);

    public ResourceKind Kind { get; init; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(24, 24);
    }

    public override void _Draw()
    {
        // Laid out on a 24x24 grid, centred in whatever size we get.
        float s = Mathf.Min(Size.X, Size.Y) / 24f;
        DrawSetTransform((Size - Vector2.One * 24 * s) / 2, 0, Vector2.One * s);
        switch (Kind)
        {
            case ResourceKind.Gold: DrawCoin(); break;
            case ResourceKind.Wood: DrawTree(); break;
            case ResourceKind.Food: DrawDrumstick(); break;
        }
    }

    void DrawCoin()
    {
        var c = new Vector2(12, 12.5f);
        DrawCircle(c, 10.5f, Outline);
        DrawCircle(c, 9.3f, new Color(0.78f, 0.55f, 0.12f));
        DrawCircle(c - new Vector2(0.6f, 0.8f), 8.2f, new Color(0.98f, 0.8f, 0.3f));
        DrawArc(c, 5.8f, 0, Mathf.Tau, 24, new Color(0.72f, 0.5f, 0.1f), 1.4f, true);
        DrawCircle(c + new Vector2(-3.2f, -3.6f), 2f, new Color(1f, 0.97f, 0.78f, 0.9f)); // shine
    }

    void DrawTree()
    {
        DrawRect(new Rect2(10, 17, 4, 6), Outline);
        DrawRect(new Rect2(11, 17, 2, 5), new Color(0.45f, 0.28f, 0.14f));
        var dark = new Color(0.08f, 0.32f, 0.1f);
        var light = new Color(0.25f, 0.62f, 0.22f);
        Vector2[][] tiers =
        [
            [new(12, 1), new(18, 8), new(6, 8)],
            [new(12, 5), new(20, 13), new(4, 13)],
            [new(12, 9), new(22, 18.5f), new(2, 18.5f)],
        ];
        foreach (var tier in tiers)
        {
            DrawColoredPolygon(tier, dark);
            DrawPolyline([tier[0], tier[1], tier[2], tier[0]], Outline, 1.2f, true);
            // Lit left half.
            DrawColoredPolygon([tier[0], (tier[1] + tier[2]) / 2, tier[2] + new Vector2(1.2f, -0.6f)], light);
        }
    }

    void DrawDrumstick()
    {
        var bone = new Color(0.93f, 0.89f, 0.8f);
        DrawLine(new Vector2(12, 12), new Vector2(4.5f, 19.5f), Outline, 5f, true);
        DrawCircle(new Vector2(3.6f, 18), 2.6f, Outline);
        DrawCircle(new Vector2(6, 20.6f), 2.6f, Outline);
        DrawLine(new Vector2(12, 12), new Vector2(4.5f, 19.5f), bone, 2.8f, true);
        DrawCircle(new Vector2(3.6f, 18), 1.6f, bone);
        DrawCircle(new Vector2(6, 20.6f), 1.6f, bone);

        var meat = new Vector2(15.5f, 8.5f);
        DrawCircle(meat, 7.2f, Outline);
        DrawCircle(meat, 6.1f, new Color(0.55f, 0.22f, 0.1f));
        DrawCircle(meat + new Vector2(-0.8f, -0.8f), 4.8f, new Color(0.78f, 0.38f, 0.18f));
        DrawCircle(meat + new Vector2(-2.2f, -2.4f), 1.5f, new Color(1f, 0.75f, 0.5f, 0.85f)); // shine
    }
}
