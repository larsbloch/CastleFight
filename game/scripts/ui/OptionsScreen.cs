using System;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Player name and display options. Display changes apply at once; everything is saved on Back.</summary>
public partial class OptionsScreen : Control
{
    static readonly int[] FrameCaps = [0, 30, 60, 120, 144, 165, 240];

    public event Action? BackRequested;

    public void Build(Settings settings)
    {
        var column = UiBuilder.Page(this, "Options", 700, titleSize: 64);

        column.AddChild(UiBuilder.SubHeading("Player"));
        var name = UiBuilder.NameField(settings.PlayerName);
        name.PlaceholderText = "Your name in lobbies";
        column.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Name"), name));

        column.AddChild(new HSeparator());
        column.AddChild(UiBuilder.SubHeading("Display"));
        var mode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mode.AddItem("Windowed", (int)DisplayMode.Windowed);
        mode.AddItem("Fullscreen (borderless)", (int)DisplayMode.Fullscreen);
        mode.AddItem("Exclusive fullscreen", (int)DisplayMode.ExclusiveFullscreen);
        mode.Select(mode.GetItemIndex((int)settings.DisplayMode));
        mode.ItemSelected += index =>
        {
            settings.DisplayMode = (DisplayMode)mode.GetItemId((int)index);
            settings.ApplyDisplay();
        };
        column.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Window"), mode));

        var fps = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (int cap in FrameCaps)
            fps.AddItem(cap == 0 ? "Unlimited" : $"{cap} fps", cap);
        int capIndex = Array.IndexOf(FrameCaps, settings.MaxFps);
        fps.Select(capIndex >= 0 ? capIndex : 0);
        fps.ItemSelected += index =>
        {
            settings.MaxFps = fps.GetItemId((int)index);
            settings.ApplyDisplay();
        };
        column.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Frame cap"), fps));

        var vsync = new CheckBox { Text = "Vertical sync (no tearing, caps at the monitor's refresh rate)", ButtonPressed = settings.VSync };
        vsync.Toggled += on =>
        {
            settings.VSync = on;
            settings.ApplyDisplay();
        };
        column.AddChild(UiBuilder.Row(UiBuilder.FieldLabel(""), vsync));

        column.AddChild(UiBuilder.Spacer(6));
        column.AddChild(UiBuilder.Row(UiBuilder.Button("Back", () =>
        {
            settings.PlayerName = Settings.CleanName(name.Text);
            settings.Save();
            BackRequested?.Invoke();
        }, minWidth: 150)));
    }
}
