using System;
using CastleFight.Net.Protocol;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Small helpers for building the (code-only) menus. The look comes from <see cref="UiTheme"/>.</summary>
static class UiBuilder
{
    /// <summary>
    /// A menu page: the title banner at the top and a centred wooden board of <paramref name="width"/> with a
    /// heading. Returns the board's content column.
    /// </summary>
    public static VBoxContainer Page(Control screen, string heading, int width, int titleSize = 76)
    {
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layout = Margins(screen, 40, 28, 40, 36);
        layout.AddChild(Title(titleSize));
        var center = new CenterContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddChild(center);
        var (board, column) = Board(heading);
        board.CustomMinimumSize = new Vector2(width, 0);
        center.AddChild(board);
        return column;
    }

    /// <summary>Full-screen margins around a vertical layout; returns the layout.</summary>
    public static VBoxContainer Margins(Control screen, int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        screen.AddChild(margin);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 16);
        margin.AddChild(layout);
        return layout;
    }

    public static TitleBanner Title(int size)
    {
        var title = new TitleBanner();
        title.Build(size);
        return title;
    }

    /// <summary>A wooden board with an optional heading; returns the board and its content column.</summary>
    public static (PanelContainer Board, VBoxContainer Column) Board(string? heading = null)
    {
        var board = new PanelContainer();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        board.AddChild(column);
        if (heading is not null)
            column.AddChild(Heading(heading));
        return (board, column);
    }

    /// <summary>Board heading: big gold capitals over an ornament.</summary>
    public static Control Heading(string text)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(new Label { Text = text.ToUpperInvariant(), ThemeTypeVariation = "HeaderLabel", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Ornament { CustomMinimumSize = new Vector2(320, 14), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
        return box;
    }

    public static Label SubHeading(string text) => new() { Text = text.ToUpperInvariant(), ThemeTypeVariation = "SubHeaderLabel" };

    public static Label Text(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>Quiet one-line text (does not wrap, so it keeps its width in a row).</summary>
    public static Label Muted(string text) => new() { Text = text, ThemeTypeVariation = "MutedLabel" };

    /// <summary>A quiet paragraph that wraps to the width of its column.</summary>
    public static Label Note(string text) =>
        new() { Text = text, ThemeTypeVariation = "MutedLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };

    /// <summary>A label of fixed width in front of a form field.</summary>
    public static Label FieldLabel(string text, int width = 150) =>
        new() { Text = text, CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center };

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var child in children)
            row.AddChild(child);
        return row;
    }

    public static Control Spacer(float height = 0) => new() { CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static Button Button(string text, Action pressed, bool expand = false, string? variation = null, int minWidth = 0)
    {
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(minWidth, 0),
            ThemeTypeVariation = variation ?? "",
        };
        button.Pressed += pressed;
        return button;
    }

    public static Button SmallButton(string text, Action pressed, string? tooltip = null)
    {
        var button = Button(text, pressed, variation: "SmallButton");
        button.TooltipText = tooltip ?? "";
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return button;
    }

    public static SpinBox PortBox(int port) =>
        new() { MinValue = 1024, MaxValue = 65535, Value = port, Rounded = true, CustomMinimumSize = new Vector2(130, 0) };

    public static LineEdit NameField(string name) =>
        new() { Text = name, MaxLength = ProtocolInfo.MaxNameLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    /// <summary>A small coloured tag such as HOST or READY.</summary>
    public static Label Badge(string text, Color color)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        label.AddThemeFontOverride("font", UiTheme.HeadingFont);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", color);
        var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.85f), BorderColor = color with { A = 0.7f } };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        box.ContentMarginTop = 1;
        box.ContentMarginBottom = 0;
        label.AddThemeStyleboxOverride("normal", box);
        return label;
    }

    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
