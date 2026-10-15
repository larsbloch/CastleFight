using System;
using System.Linq;
using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// Lobby: the map, both armies with their open places, the ready check, and chat. Everything is server
/// state; this screen only sends requests and redraws whenever a new <see cref="LobbyStateMessage"/> arrives.
/// </summary>
public partial class LobbyScreen : Control
{
    LockstepClient _client = null!;
    string _where = "";
    bool _built;
    Label _connection = null!;
    MapSelector _maps = null!;
    HBoxContainer _teams = null!;
    Label _status = null!;
    HBoxContainer _actions = null!;

    public event Action? LeaveRequested;

    public void Build(LockstepClient client, string where)
    {
        _client = client;
        _where = where;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _client.LobbyChanged += OnLobbyChanged;
        if (_client.Lobby is null)
            ShowConnecting();
        else
            BuildLobby();
    }

    public override void _ExitTree() => _client.LobbyChanged -= OnLobbyChanged;

    void OnLobbyChanged(LobbyStateMessage lobby)
    {
        if (!_built)
        {
            UiBuilder.Clear(this);
            BuildLobby();
        }
        else
        {
            Refresh();
        }
    }

    void ShowConnecting()
    {
        var column = UiBuilder.Page(this, "Lobby", 560, titleSize: 64);
        var text = UiBuilder.Text($"Connecting to {_where}…");
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(text);
        column.AddChild(UiBuilder.Row(UiBuilder.Spacer(), UiBuilder.Button("Cancel", () => LeaveRequested?.Invoke(), minWidth: 160), UiBuilder.Spacer()));
    }

    void BuildLobby()
    {
        _built = true;
        var layout = UiBuilder.Margins(this, 36, 14, 36, 26);
        layout.AddThemeConstantOverride("separation", 8);
        layout.AddChild(UiBuilder.Title(50));

        var columns = UiBuilder.Row();
        columns.AddThemeConstantOverride("separation", 20);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        layout.AddChild(columns);

        var (left, leftColumn) = UiBuilder.Board("Lobby");
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1.75f;
        _connection = UiBuilder.Muted("");
        _connection.HorizontalAlignment = HorizontalAlignment.Center;
        leftColumn.AddChild(_connection);

        leftColumn.AddChild(UiBuilder.SubHeading("Battlefield"));
        _maps = new MapSelector();
        _maps.Build(_client.Lobby!.Map, new Vector2(176, 92));
        _maps.MapChosen += map => _client.SetMap(map);
        leftColumn.AddChild(_maps);
        leftColumn.AddChild(new HSeparator());

        _teams = UiBuilder.Row();
        _teams.AddThemeConstantOverride("separation", 18);
        _teams.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftColumn.AddChild(_teams);

        _status = UiBuilder.Text("");
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _actions = UiBuilder.Row();
        _actions.AddThemeConstantOverride("separation", 14);
        leftColumn.AddChild(UiBuilder.Row(_status, _actions));
        columns.AddChild(left);

        var (right, rightColumn) = UiBuilder.Board("Chat");
        right.CustomMinimumSize = new Vector2(440, 0);
        var chat = new ChatPanel { SizeFlagsVertical = SizeFlags.ExpandFill };
        chat.Build(_client);
        rightColumn.AddChild(chat);
        columns.AddChild(right);

        Refresh();
    }

    void Refresh()
    {
        if (_client.Lobby is not { } lobby)
            return;
        bool isHost = _client.IsHost;
        string host = isHost ? "you are the host" : $"hosted by {lobby.Slots[lobby.HostSlot].Name}";
        _connection.Text = $"{_where}  ·  {host}";

        _maps.Select(lobby.Map);
        _maps.Editable = isHost;

        UiBuilder.Clear(_teams);
        for (int team = 0; team < Simulation.TeamCount; team++)
            _teams.AddChild(TeamColumn(lobby, team, isHost));

        UiBuilder.Clear(_actions);
        var leave = UiBuilder.Button("Leave", () => LeaveRequested?.Invoke(), minWidth: 140);
        int waiting = lobby.Slots.Where((s, i) => s.Kind == SlotKind.Human && !s.Ready && i != lobby.HostSlot).Count();
        if (isHost)
        {
            _status.Text = waiting > 0 ? $"Waiting for {waiting} player{(waiting == 1 ? "" : "s")} to get ready." : "Everyone is ready.";
            _status.AddThemeColorOverride("font_color", waiting > 0 ? UiTheme.Muted : UiTheme.Good);
            var start = UiBuilder.Button("Start Battle", () => _client.StartGame(), minWidth: 220);
            start.Disabled = waiting > 0;
            _actions.AddChild(leave);
            _actions.AddChild(start);
        }
        else
        {
            bool ready = lobby.Slots[_client.LocalSlot].Ready;
            _status.Text = ready ? "Waiting for the host to start the battle." : "Tick Ready when you are set.";
            _status.AddThemeColorOverride("font_color", UiTheme.Muted);
            var readyBox = new CheckBox { Text = "Ready", ButtonPressed = ready, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            readyBox.AddThemeFontOverride("font", UiTheme.HeadingFont);
            readyBox.AddThemeFontSizeOverride("font_size", 22);
            readyBox.Toggled += on => _client.SetReady(on);
            _actions.AddChild(readyBox);
            _actions.AddChild(leave);
        }
    }

    Control TeamColumn(LobbyStateMessage lobby, int team, bool isHost)
    {
        var well = new PanelContainer { ThemeTypeVariation = "InsetPanel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        well.AddChild(column);

        int size = TeamSize(lobby, team);
        var name = new Label { Text = UiTheme.TeamNames[team].ToUpperInvariant(), ThemeTypeVariation = "SubHeaderLabel", VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeColorOverride("font_color", UiTheme.TeamText[team]);
        name.AddThemeFontSizeOverride("font_size", 21);
        var count = UiBuilder.Muted($"{size}/{ProtocolInfo.TeamCapacity}");
        count.VerticalAlignment = VerticalAlignment.Center;
        var header = UiBuilder.Row(name, count, UiBuilder.Spacer());
        bool localHere = lobby.Slots[_client.LocalSlot].Team == team;
        if (!localHere && size < ProtocolInfo.TeamCapacity)
            header.AddChild(UiBuilder.SmallButton("Join", () => _client.SetTeam(team), $"Switch to the {UiTheme.TeamNames[team]}"));
        column.AddChild(header);

        for (int slot = 0; slot < lobby.Slots.Length; slot++)
        {
            if (lobby.Slots[slot].Kind != SlotKind.Empty && lobby.Slots[slot].Team == team)
                column.AddChild(PlayerRow(lobby, slot, isHost));
        }
        bool lobbyFull = lobby.Slots.All(s => s.Kind != SlotKind.Empty);
        for (int open = size; open < ProtocolInfo.TeamCapacity; open++)
            column.AddChild(OpenRow(team, isHost && !lobbyFull));
        return well;
    }

    Control PlayerRow(LobbyStateMessage lobby, int slot, bool isHost)
    {
        var s = lobby.Slots[slot];
        bool you = slot == _client.LocalSlot;
        var row = SlotFrame(UnitRenderer.TeamColors[s.Team], filled: true);
        var content = UiBuilder.Row();
        row.AddChild(content);

        var name = new Label { Text = s.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, VerticalAlignment = VerticalAlignment.Center };
        if (you)
        {
            name.AddThemeFontOverride("font", UiTheme.BodyBoldFont);
            name.Text += "  (you)";
        }
        content.AddChild(name);

        if (slot == lobby.HostSlot)
            content.AddChild(UiBuilder.Badge("HOST", UiTheme.Gold));
        if (s.Kind == SlotKind.Bot)
            content.AddChild(UiBuilder.Badge("BOT", UiTheme.Muted));
        else if (slot != lobby.HostSlot)
            content.AddChild(s.Ready ? UiBuilder.Badge("READY", UiTheme.Good) : UiBuilder.Badge("NOT READY", UiTheme.Warning));

        int other = 1 - s.Team;
        if (isHost && !you && TeamSize(lobby, other) < ProtocolInfo.TeamCapacity)
            content.AddChild(UiBuilder.SmallButton("Move", () => _client.SetSlotTeam(slot, other), $"Move to the {UiTheme.TeamNames[other]}"));
        if (isHost && s.Kind == SlotKind.Bot)
            content.AddChild(UiBuilder.SmallButton("Remove", () => _client.RemoveBot(slot)));
        return row;
    }

    Control OpenRow(int team, bool canAddBot)
    {
        var row = SlotFrame(UnitRenderer.TeamColors[team], filled: false);
        var content = UiBuilder.Row();
        row.AddChild(content);
        var open = UiBuilder.Muted("Open");
        open.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        open.VerticalAlignment = VerticalAlignment.Center;
        content.AddChild(open);
        if (canAddBot)
            content.AddChild(UiBuilder.SmallButton("Add Bot", () => _client.AddBot(team)));
        return row;
    }

    static PanelContainer SlotFrame(Color teamColor, bool filled)
    {
        var style = new StyleBoxFlat
        {
            BgColor = filled ? new Color(0.17f, 0.12f, 0.08f, 0.9f) : new Color(0.1f, 0.07f, 0.05f, 0.45f),
            BorderColor = filled ? teamColor : teamColor with { A = 0.3f },
            BorderWidthLeft = 4,
        };
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 8;
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 42) };
        frame.AddThemeStyleboxOverride("panel", style);
        return frame;
    }

    static int TeamSize(LobbyStateMessage lobby, int team) => lobby.Slots.Count(s => s.Kind != SlotKind.Empty && s.Team == team);
}
