using Godot;

namespace CastleFight.Game.UI;

/// <summary>What one slot of the command HUD offers: a name, an optional gold cost and a hotkey.</summary>
public readonly record struct CommandInfo(string Name, int Cost, string Hotkey, bool Affordable, bool Active);

/// <summary>
/// A button filling one slot of the command HUD: the hotkey in the corner, the name in the middle and the cost
/// below it (red when it can't be paid). Placeholder until there are icons.
/// </summary>
public partial class CommandButton : Button
{
    static readonly Color HoverRim = new(1f, 0.85f, 0.45f, 0.8f);
    static readonly Color ActiveRim = new(0.45f, 1f, 0.45f, 0.95f);

    readonly Label _hotkey = new() { ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _name = new()
    {
        ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    readonly Label _cost = new() { ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
    CommandInfo? _info;

    public override void _Ready()
    {
        Flat = true;
        FocusMode = FocusModeEnum.None;
        ClipContents = true;
        foreach (var label in new[] { _hotkey, _name, _cost })
            AddChild(label);
        _hotkey.AddThemeFontSizeOverride("font_size", 11);
        _name.AddThemeFontSizeOverride("font_size", 12);
        _cost.AddThemeFontSizeOverride("font_size", 12);
        _hotkey.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _hotkey.Position = new Vector2(4, 1);
        _name.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _name.OffsetBottom = -10;
        _cost.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _cost.OffsetTop = -17;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        Visible = false;
    }

    /// <summary>Null empties the slot.</summary>
    public void SetCommand(CommandInfo? info)
    {
        if (info == _info)
            return;
        _info = info;
        Visible = info is not null;
        if (info is not { } c)
            return;
        _hotkey.Text = c.Hotkey;
        _name.Text = c.Name;
        _cost.Text = c.Cost > 0 ? c.Cost.ToString() : "";
        _cost.AddThemeColorOverride("font_color", c.Affordable ? UiTheme.Gold : UiTheme.Warning);
        TooltipText = c.Cost > 0 ? $"{c.Name} ({c.Hotkey})\n{c.Cost} gold" : $"{c.Name} ({c.Hotkey})";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_info is { Active: true })
            DrawRect(new Rect2(Vector2.Zero, Size).Grow(-1), ActiveRim, filled: false, width: 2.5f);
        else if (IsHovered())
            DrawRect(new Rect2(Vector2.Zero, Size).Grow(-1), HoverRim, filled: false, width: 2f);
    }
}
