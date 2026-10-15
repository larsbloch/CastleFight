using System;
using System.Linq;
using CastleFight.Game.UI;
using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Net.Relay;
using CastleFight.Net.Sessions;
using CastleFight.Net.Transport;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Root of the game (Main.tscn). Switches between the main menu, the lobby and a match, and owns the
/// network connection plus, when hosting, the in-process relay.
/// </summary>
public partial class App : Node
{
    LaunchOptions _options = null!;
    Settings _settings = null!;
    Node? _screen;
    LockstepClient? _client;
    RelayHost? _relay;
    string _where = "";
    ulong _seed = 1;
    MapId _singlePlayerMap;
    int _singlePlayerTeamSize;
    bool _autoStartSetupDone;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        _settings = Settings.Load();
        if (_options.Name is { } name)
            _settings.PlayerName = Settings.CleanName(name);

        if (_options.Host)
            Host(_options.Port);
        else if (_options.JoinAddress is { } address)
            Join(address, _options.Port);
        else if (_options.SkipMenu)
            StartSinglePlayer(_options.Map ?? MapId.SingleLane, _options.PlayersPerTeam);
        else
            ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        // In a match the session polls the client; before that, we do.
        if (_client is not null && _screen is not MatchView)
        {
            _client.Poll();
            if (_options.SkipMenu)
                AutoLobby();
        }
        if (_relay?.Error is { } error)
        {
            _relay = null;
            ShowMainMenu($"The relay stopped: {error.Message}");
        }
    }

    // ---- Screens ----

    void ShowScreen(Node screen)
    {
        if (_screen is not null)
        {
            RemoveChild(_screen);
            _screen.QueueFree();
        }
        _screen = screen;
        AddChild(screen);
    }

    void ShowMainMenu(string? status = null)
    {
        CloseNetwork();
        var menu = new MainMenu();
        menu.Build(_settings, status);
        menu.SinglePlayerRequested += (map, teamSize) =>
        {
            RememberName(menu.PlayerName);
            StartSinglePlayer(map, teamSize);
        };
        menu.HostRequested += port =>
        {
            RememberName(menu.PlayerName);
            _settings.Port = port;
            Host(port);
        };
        menu.JoinRequested += (address, port) =>
        {
            RememberName(menu.PlayerName);
            if (address.Length == 0)
            {
                menu.ShowStatus("Enter the host's address.");
                return;
            }
            _settings.Address = address;
            _settings.Port = port;
            Join(address, port);
        };
        menu.QuitRequested += () => GetTree().Quit();
        ShowScreen(menu);
    }

    void RememberName(string name)
    {
        _settings.PlayerName = Settings.CleanName(name);
        _settings.Save();
    }

    void StartSinglePlayer(MapId map, int playersPerTeam)
    {
        _singlePlayerMap = map;
        _singlePlayerTeamSize = playersPerTeam;
        var setup = GameSetup.ForTeams(_seed, playersPerTeam, map);
        ShowMatch(new LocalSession(setup, localPlayerId: 0));
    }

    void ShowMatch(IMatchSession session)
    {
        var view = new MatchView();
        view.Init(session, _options);
        view.LeaveRequested += () => ShowMainMenu();
        view.RestartRequested += () =>
        {
            _seed++;
            StartSinglePlayer(_singlePlayerMap, _singlePlayerTeamSize);
        };
        ShowScreen(view);
    }

    // ---- Multiplayer ----

    void Host(int port)
    {
        CloseNetwork();
        try
        {
            _relay = new RelayHost(port, log: message => GD.Print($"[relay] {message}"));
        }
        catch (Exception ex)
        {
            ShowMainMenu($"Could not host: {ex.Message}");
            return;
        }
        Connect("127.0.0.1", _relay.Port, $"your game on port {_relay.Port}");
    }

    void Join(string address, int port)
    {
        CloseNetwork();
        Connect(address, port, $"{address}:{port}");
    }

    void Connect(string address, int port, string where)
    {
        _where = where;
        _autoStartSetupDone = false;
        LiteNetClientTransport transport;
        try
        {
            transport = new LiteNetClientTransport(address, port);
        }
        catch (Exception ex)
        {
            ShowMainMenu($"Could not connect: {ex.Message}");
            return;
        }

        var client = new LockstepClient(transport, _settings.PlayerName);
        _client = client;
        client.GameStarted += _ => ShowMatch(new NetworkSession(client, autoPlayLocal: _options.AutoPlay));
        client.Disconnected += reason =>
        {
            // In a match the view shows the error; in the lobby we go back to the menu.
            if (_screen is not MatchView)
                ShowMainMenu(client.State == ClientState.Rejected ? $"Rejected: {reason}" : $"Disconnected: {reason}");
        };

        var lobby = new LobbyScreen();
        lobby.Build(client, where);
        lobby.LeaveRequested += () => ShowMainMenu();
        ShowScreen(lobby);
    }

    /// <summary>Command-line lobby automation (--bots, --autostart, --ready) for multi-window and headless tests.</summary>
    void AutoLobby()
    {
        if (_client is not { State: ClientState.Lobby, Lobby: { } lobby })
            return;

        if (!_autoStartSetupDone)
        {
            _autoStartSetupDone = true;
            if (_client.IsHost)
            {
                if (_options.Map is { } map)
                    _client.SetMap(map);
                for (int i = 0; i < _options.Bots; i++)
                    _client.AddBot(i % Simulation.TeamCount);
            }
            else if (_options.Ready)
            {
                _client.SetReady(true);
            }
        }

        if (_client.IsHost && _options.AutoStartPlayers > 0)
        {
            int humans = lobby.Slots.Count(s => s.Kind == SlotKind.Human);
            bool everyoneReady = lobby.Slots.Where((s, i) => s.Kind == SlotKind.Human && i != lobby.HostSlot).All(s => s.Ready);
            if (humans >= _options.AutoStartPlayers && everyoneReady)
                _client.StartGame();
        }
    }

    void CloseNetwork()
    {
        // The match view disposes its session (and with it the client) when it leaves the tree.
        if (_screen is not MatchView)
            _client?.Dispose();
        _client = null;
        _relay?.Dispose();
        _relay = null;
    }

    public override void _ExitTree() => CloseNetwork();
}
