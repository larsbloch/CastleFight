using System;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Game options before hosting: map and port. Bots are added in the lobby.</summary>
public partial class HostScreen : Control
{
    Settings _settings = null!;
    MapSelector _maps = null!;
    SpinBox _port = null!;

    public event Action? OpenLobbyRequested;
    public event Action? BackRequested;

    /// <summary>The choices are written back to <paramref name="settings"/> before an event fires.</summary>
    public void Build(Settings settings, string? status)
    {
        _settings = settings;
        var column = UiBuilder.Page(this, "Host Game", 940, titleSize: 64);

        column.AddChild(UiBuilder.SubHeading("Battlefield"));
        _maps = new MapSelector();
        _maps.Build(settings.HostMap, new Vector2(200, 106));
        column.AddChild(_maps);
        column.AddChild(new HSeparator());

        column.AddChild(UiBuilder.SubHeading("Connection"));
        _port = UiBuilder.PortBox(settings.HostPort);
        column.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("UDP port", 120), _port));
        column.AddChild(UiBuilder.Note("Friends outside your network need this UDP port forwarded to this PC, and the game allowed through the Windows firewall. Bots are added in the lobby."));

        if (!string.IsNullOrEmpty(status))
            column.AddChild(UiBuilder.Text(status, UiTheme.Warning));

        column.AddChild(UiBuilder.Spacer(4));
        var back = UiBuilder.Button("Back", () => BackRequested?.Invoke(), minWidth: 150);
        var open = UiBuilder.Button("Open Lobby", () => Fire(OpenLobbyRequested), minWidth: 210);
        column.AddChild(UiBuilder.Row(back, UiBuilder.Spacer(), open));
    }

    void Fire(Action? action)
    {
        _settings.HostMap = _maps.Selected;
        _settings.HostPort = (int)_port.Value;
        action?.Invoke();
    }
}
