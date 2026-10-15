using System;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Small helpers for building the (code-only) menus.</summary>
static class UiBuilder
{
    /// <summary>Full-screen control with a centred panel; returns the panel's content column.</summary>
    public static VBoxContainer CenteredPanel(Control screen, int minWidth)
    {
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0) };
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        return column;
    }

    public static Label Title(string text, int size = 28)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static Label Text(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var child in children)
            row.AddChild(child);
        return row;
    }

    public static Button Button(string text, Action pressed, bool expand = false)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill };
        button.Pressed += pressed;
        return button;
    }

    public static OptionButton MapPicker(MapId selected)
    {
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var map in MapCatalog.All)
            picker.AddItem(MapCatalog.DisplayName(map), (int)map);
        picker.Select(picker.GetItemIndex((int)selected));
        return picker;
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
