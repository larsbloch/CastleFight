using System;
using CastleFight.Net.Protocol;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Game options before hosting: map, starting bots and port. Also starts an offline game with the same setup.</summary>
public partial class HostScreen : Control
{
    Settings _settings = null!;
    MapSelector _maps = null!;
    SpinBox _blueBots = null!;
    SpinBox _redBots = null!;
    SpinBox _port = null!;

    public event Action? OpenLobbyRequested;
    public event Action? PlayOfflineRequested;
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

        var sections = UiBuilder.Row();
        sections.AddThemeConstantOverride("separation", 36);
        column.AddChild(sections);

        var armies = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        armies.AddChild(UiBuilder.SubHeading("Bots"));
        _blueBots = BotBox(settings.HostBlueBots, ProtocolInfo.TeamCapacity - 1);
        _redBots = BotBox(settings.HostRedBots, ProtocolInfo.TeamCapacity);
        armies.AddChild(UiBuilder.Row(TeamLabel(0), _blueBots, UiBuilder.Muted("your allies")));
        armies.AddChild(UiBuilder.Row(TeamLabel(1), _redBots, UiBuilder.Muted("your enemies")));
        armies.AddChild(UiBuilder.Note("Friends who join take the open places. Bots can be added, moved and removed in the lobby too."));
        sections.AddChild(armies);

        var connection = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        connection.AddChild(UiBuilder.SubHeading("Connection"));
        _port = UiBuilder.PortBox(settings.HostPort);
        connection.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("UDP port", 120), _port));
        connection.AddChild(UiBuilder.Note("Friends outside your network need this UDP port forwarded to this PC, and the game allowed through the Windows firewall."));
        sections.AddChild(connection);

        if (!string.IsNullOrEmpty(status))
            column.AddChild(UiBuilder.Text(status, UiTheme.Warning));

        column.AddChild(UiBuilder.Spacer(4));
        var back = UiBuilder.Button("Back", () => BackRequested?.Invoke(), minWidth: 150);
        var offline = UiBuilder.Button("Play Offline", () => Fire(PlayOfflineRequested), minWidth: 190);
        offline.TooltipText = "Play against the bots on this PC only. Space pauses, R restarts.";
        var open = UiBuilder.Button("Open Lobby", () => Fire(OpenLobbyRequested), minWidth: 210);
        column.AddChild(UiBuilder.Row(back, UiBuilder.Spacer(), offline, open));
    }

    void Fire(Action? action)
    {
        _settings.HostMap = _maps.Selected;
        _settings.HostBlueBots = (int)_blueBots.Value;
        _settings.HostRedBots = (int)_redBots.Value;
        _settings.HostPort = (int)_port.Value;
        action?.Invoke();
    }

    static SpinBox BotBox(int value, int max) =>
        new() { MinValue = 0, MaxValue = max, Value = Math.Clamp(value, 0, max), Rounded = true, CustomMinimumSize = new Vector2(110, 0) };

    static Label TeamLabel(int team)
    {
        var label = UiBuilder.FieldLabel(UiTheme.TeamNames[team], 120);
        label.AddThemeColorOverride("font_color", UiTheme.TeamText[team]);
        return label;
    }
}
