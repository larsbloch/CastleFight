using System;
using CastleFight.Net.Protocol;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Title screen: Host game / Join game / Options / Exit.</summary>
public partial class MainMenu : Control
{
    public event Action? HostRequested;
    public event Action? JoinRequested;
    public event Action? OptionsRequested;
    public event Action? QuitRequested;

    public void Build(Settings settings, string? status)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var layout = UiBuilder.Margins(this, 40, 64, 40, 22);
        layout.AddChild(UiBuilder.Title(116));
        layout.AddChild(UiBuilder.Spacer(18));

        var (board, column) = UiBuilder.Board();
        board.CustomMinimumSize = new Vector2(430, 0);
        column.AddThemeConstantOverride("separation", 14);
        foreach (var (text, action) in new (string, Action)[]
                 {
                     ("Host Game", () => HostRequested?.Invoke()),
                     ("Join Game", () => JoinRequested?.Invoke()),
                     ("Options", () => OptionsRequested?.Invoke()),
                     ("Exit", () => QuitRequested?.Invoke()),
                 })
        {
            var button = UiBuilder.Button(text, action, variation: "BigButton");
            button.CustomMinimumSize = new Vector2(0, 66);
            column.AddChild(button);
        }
        var center = new CenterContainer();
        center.AddChild(board);
        layout.AddChild(center);

        var statusLabel = UiBuilder.Text(status ?? "", UiTheme.Warning);
        statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        layout.AddChild(statusLabel);

        layout.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var playingAs = UiBuilder.Muted($"Playing as {settings.PlayerName}");
        playingAs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var version = UiBuilder.Muted($"Protocol {ProtocolInfo.Version}  ·  build {ProtocolInfo.BuildId.ToString()[..8]}");
        layout.AddChild(UiBuilder.Row(playingAs, version));
    }
}
