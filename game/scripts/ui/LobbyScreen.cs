using System;
using System.Linq;
using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// Lobby: two team columns, map choice, bots, ready check. Everything is server state; this screen only
/// sends requests and redraws whenever a new <see cref="LobbyStateMessage"/> arrives.
/// </summary>
public partial class LobbyScreen : Control
{
    static readonly string[] TeamNames = ["Team 1 (blue)", "Team 2 (red)"];

    LockstepClient _client = null!;
    string _where = "";
    VBoxContainer _content = null!;

    public event Action? LeaveRequested;

    public void Build(LockstepClient client, string where)
    {
        _client = client;
        _where = where;
        _content = UiBuilder.CenteredPanel(this, 640);
        _client.LobbyChanged += OnLobbyChanged;
        Redraw();
    }

    public override void _ExitTree() => _client.LobbyChanged -= OnLobbyChanged;

    void OnLobbyChanged(LobbyStateMessage lobby) => Redraw();

    void Redraw()
    {
        UiBuilder.Clear(_content);
        _content.AddChild(UiBuilder.Title("Lobby"));

        if (_client.Lobby is not { } lobby)
        {
            _content.AddChild(UiBuilder.Text(_client.State == ClientState.Connecting ? $"Connecting to {_where}…" : "Joining…"));
            _content.AddChild(UiBuilder.Button("Cancel", () => LeaveRequested?.Invoke()));
            return;
        }

        bool isHost = _client.IsHost;
        _content.AddChild(UiBuilder.Text($"Connected to {_where}. {(isHost ? "You are the host." : "The host picks the map and starts the game.")}"));

        var map = UiBuilder.MapPicker(lobby.Map);
        map.Disabled = !isHost;
        map.ItemSelected += index => _client.SetMap((MapId)map.GetItemId((int)index));
        _content.AddChild(UiBuilder.Row(new Label { Text = "Map" }, map));

        var teams = UiBuilder.Row();
        for (int team = 0; team < Simulation.TeamCount; team++)
            teams.AddChild(TeamColumn(lobby, team, isHost));
        _content.AddChild(teams);

        var actions = UiBuilder.Row();
        if (isHost)
        {
            for (int team = 0; team < Simulation.TeamCount; team++)
            {
                int t = team;
                var add = UiBuilder.Button($"Add bot to team {team + 1}", () => _client.AddBot(t));
                add.Disabled = TeamSize(lobby, team) >= ProtocolInfo.TeamCapacity || lobby.Slots.All(s => s.Kind != SlotKind.Empty);
                actions.AddChild(add);
            }
            bool everyoneReady = lobby.Slots.Select((s, i) => (s, i)).All(x => x.s.Kind != SlotKind.Human || x.s.Ready || x.i == _client.LocalSlot);
            var start = UiBuilder.Button("Start game", () => _client.StartGame(), expand: true);
            start.Disabled = !everyoneReady;
            actions.AddChild(start);
        }
        else
        {
            var ready = new CheckButton { Text = "Ready", ButtonPressed = lobby.Slots[_client.LocalSlot].Ready, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            ready.Toggled += on => _client.SetReady(on);
            actions.AddChild(ready);
        }
        actions.AddChild(UiBuilder.Button("Leave", () => LeaveRequested?.Invoke()));
        _content.AddChild(actions);

        int waiting = lobby.Slots.Where((s, i) => s.Kind == SlotKind.Human && !s.Ready && i != lobby.HostSlot).Count();
        if (isHost && waiting > 0)
            _content.AddChild(UiBuilder.Text($"Waiting for {waiting} player(s) to get ready.", new Color(0.8f, 0.8f, 0.6f)));
    }

    VBoxContainer TeamColumn(LobbyStateMessage lobby, int team, bool isHost)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = UiBuilder.Title($"{TeamNames[team]}  {TeamSize(lobby, team)}/{ProtocolInfo.TeamCapacity}", 16);
        title.AddThemeColorOverride("font_color", UnitRenderer.TeamColors[team]);
        column.AddChild(title);

        for (int slot = 0; slot < lobby.Slots.Length; slot++)
        {
            var s = lobby.Slots[slot];
            if (s.Kind == SlotKind.Empty || s.Team != team)
                continue;

            string tags = s.Kind == SlotKind.Bot ? " [bot]"
                : slot == lobby.HostSlot ? " [host]"
                : s.Ready ? " [ready]" : "";
            string you = slot == _client.LocalSlot ? " (you)" : "";
            var row = UiBuilder.Row(new Label { Text = s.Name + tags + you, SizeFlagsHorizontal = SizeFlags.ExpandFill });

            int other = 1 - team;
            bool otherHasRoom = TeamSize(lobby, other) < ProtocolInfo.TeamCapacity;
            int index = slot;
            if (slot == _client.LocalSlot && otherHasRoom)
                row.AddChild(UiBuilder.Button("Switch", () => _client.SetTeam(other)));
            else if (isHost && otherHasRoom)
                row.AddChild(UiBuilder.Button("Move", () => _client.SetSlotTeam(index, other)));
            if (isHost && s.Kind == SlotKind.Bot)
                row.AddChild(UiBuilder.Button("Remove", () => _client.RemoveBot(index)));
            column.AddChild(row);
        }
        return column;
    }

    static int TeamSize(LobbyStateMessage lobby, int team) => lobby.Slots.Count(s => s.Kind != SlotKind.Empty && s.Team == team);
}
