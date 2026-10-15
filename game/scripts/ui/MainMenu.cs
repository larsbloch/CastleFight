using System;
using CastleFight.Net.Protocol;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Single player / Host / Join / Quit.</summary>
public partial class MainMenu : Control
{
    LineEdit _name = null!;
    OptionButton _map = null!;
    SpinBox _playersPerTeam = null!;
    SpinBox _hostPort = null!;
    LineEdit _address = null!;
    SpinBox _joinPort = null!;
    Label _status = null!;

    public event Action<MapId, int>? SinglePlayerRequested;
    public event Action<int>? HostRequested;
    public event Action<string, int>? JoinRequested;
    public event Action? QuitRequested;

    public string PlayerName => _name.Text;

    public void Build(Settings settings, string? status)
    {
        var column = UiBuilder.CenteredPanel(this, 460);
        column.AddChild(UiBuilder.Title("Castle Fight", 36));

        _name = new LineEdit { Text = settings.PlayerName, MaxLength = ProtocolInfo.MaxNameLength, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(UiBuilder.Row(new Label { Text = "Name" }, _name));

        column.AddChild(new HSeparator());
        column.AddChild(UiBuilder.Title("Single player", 18));
        _map = UiBuilder.MapPicker(MapId.SingleLane);
        _playersPerTeam = new SpinBox { MinValue = 1, MaxValue = ProtocolInfo.TeamCapacity, Value = 3 };
        column.AddChild(UiBuilder.Row(new Label { Text = "Map" }, _map));
        column.AddChild(UiBuilder.Row(new Label { Text = "Players per team" }, _playersPerTeam,
            UiBuilder.Button("Play vs bots", () => SinglePlayerRequested?.Invoke((MapId)_map.GetSelectedId(), (int)_playersPerTeam.Value), expand: true)));

        column.AddChild(new HSeparator());
        column.AddChild(UiBuilder.Title("Multiplayer", 18));
        _hostPort = PortBox(settings.Port);
        column.AddChild(UiBuilder.Row(new Label { Text = "Port" }, _hostPort,
            UiBuilder.Button("Host game", () => HostRequested?.Invoke((int)_hostPort.Value), expand: true)));
        _address = new LineEdit { Text = settings.Address, PlaceholderText = "Host address", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _joinPort = PortBox(settings.Port);
        column.AddChild(UiBuilder.Row(_address, _joinPort,
            UiBuilder.Button("Join", () => JoinRequested?.Invoke(_address.Text.Trim(), (int)_joinPort.Value))));
        column.AddChild(UiBuilder.Text($"Hosting needs UDP port {ProtocolInfo.DefaultPort} (or the one you pick) forwarded to this PC for players outside your network.",
            new Color(0.7f, 0.7f, 0.7f)));

        column.AddChild(new HSeparator());
        column.AddChild(UiBuilder.Button("Quit", () => QuitRequested?.Invoke()));
        _status = UiBuilder.Text(status ?? "", new Color(1f, 0.55f, 0.45f));
        column.AddChild(_status);
    }

    public void ShowStatus(string text) => _status.Text = text;

    static SpinBox PortBox(int port) => new() { MinValue = 1024, MaxValue = 65535, Value = port, Rounded = true };
}
