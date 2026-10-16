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
/// Root of the game (Main.tscn). Switches between the menus, the lobby and a match, and owns the network
/// connection plus, when hosting, the in-process relay.
/// </summary>
public partial class App : Node
{
    /// <summary>Menus are laid out for this size and scaled to the window; matches render at native size.</summary>
    static readonly Vector2I MenuBaseSize = new(1600, 900);

    /// <summary>What the host asked for in the setup screen, applied once the lobby is open.</summary>
    sealed record LobbySetup(MapId? Map, int BlueBots, int RedBots, bool Ready);

    LaunchOptions _options = null!;
    Settings _settings = null!;
    Control _menuRoot = null!;
    Node? _screen;
    LockstepClient? _client;
    RelayHost? _relay;
    LobbySetup? _pendingSetup;
    bool _sayDone;
    ulong _seed = 1;
    (MapId Map, int BlueBots, int RedBots) _offline;
    int _frames;

    /// <summary>Where to go when the connection fails or the player leaves the lobby.</summary>
    Action<string?> _back = null!;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        _settings = Settings.Load();
        if (_options.Name is { } name)
            _settings.PlayerName = Settings.CleanName(name);
        if (_options.ScreenshotPath is null && !_options.SkipMenu)
            _settings.ApplyDisplay();

        // Themes pass down through Controls only, so the menus live under one themed full-screen Control.
        _menuRoot = new Control { Theme = UiTheme.Build(), MouseFilter = Control.MouseFilterEnum.Ignore };
        _menuRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_menuRoot);
        _menuRoot.AddChild(new Backdrop());
        _back = ShowMainMenu;

        if (_options.Host)
        {
            _pendingSetup = new LobbySetup(_options.Map, (_options.Bots + 1) / 2, _options.Bots / 2, false);
            Host(_options.Port);
        }
        else if (_options.JoinAddress is { } address)
        {
            _pendingSetup = new LobbySetup(null, 0, 0, _options.Ready);
            Join(address, _options.Port);
        }
        else if (_options.SkipMenu)
        {
            int perTeam = _options.PlayersPerTeam;
            StartOffline(_options.Map ?? MapId.SingleLane, perTeam - 1, perTeam);
        }
        else
        {
            switch (_options.Screen)
            {
                case "host": ShowHostSetup(); break;
                case "join": ShowJoin(); break;
                case "options": ShowOptions(); break;
                default: ShowMainMenu(); break;
            }
        }
    }

    public override void _Process(double delta)
    {
        // In a match the session polls the client; before that, we do.
        if (_client is not null && _screen is not MatchView)
        {
            _client.Poll();
            ApplyLobbySetup();
            if (_options.SkipMenu)
                AutoStart();
        }
        if (_relay?.Error is { } error)
        {
            _relay = null;
            ShowHostSetup($"The relay stopped: {error.Message}");
        }
        if (_options.ScreenshotPath is { } path && ++_frames == _options.ScreenshotFrames)
        {
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"Screenshot saved to {path}");
            GetTree().Quit();
        }
    }

    // ---- Screens ----

    void ShowScreen(Node screen)
    {
        if (_screen is not null)
        {
            _screen.GetParent().RemoveChild(_screen);
            _screen.QueueFree();
        }
        _screen = screen;
        bool menu = screen is Control;
        _menuRoot.Visible = menu;
        var root = GetTree().Root;
        root.ContentScaleMode = menu ? Window.ContentScaleModeEnum.CanvasItems : Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        root.ContentScaleSize = MenuBaseSize;
        (menu ? (Node)_menuRoot : this).AddChild(screen);
    }

    void ShowMainMenu(string? status = null)
    {
        CloseNetwork();
        var menu = new MainMenu();
        menu.Build(_settings, status);
        menu.HostRequested += () => ShowHostSetup();
        menu.JoinRequested += () => ShowJoin();
        menu.OptionsRequested += ShowOptions;
        menu.QuitRequested += () => GetTree().Quit();
        ShowScreen(menu);
    }

    void ShowHostSetup(string? status = null)
    {
        CloseNetwork();
        var screen = new HostScreen();
        screen.Build(_settings, status);
        screen.BackRequested += () => ShowMainMenu();
        screen.OpenLobbyRequested += () =>
        {
            _settings.Save();
            _pendingSetup = new LobbySetup(_settings.HostMap, 0, 0, false);
            Host(_settings.HostPort);
        };
        ShowScreen(screen);
    }

    void ShowJoin(string? status = null)
    {
        CloseNetwork();
        var screen = new JoinScreen();
        screen.Build(_settings, status);
        screen.BackRequested += () => ShowMainMenu();
        screen.JoinRequested += (address, port) =>
        {
            _settings.AddRecent(address, port);
            _settings.Save();
            _pendingSetup = null;
            Join(address, port);
        };
        ShowScreen(screen);
    }

    void ShowOptions()
    {
        var screen = new OptionsScreen();
        screen.Build(_settings);
        screen.BackRequested += () => ShowMainMenu();
        ShowScreen(screen);
    }

    /// <summary>Single player against bots on this PC (command line only): you, your bot allies, then the enemy bots.</summary>
    void StartOffline(MapId map, int blueBots, int redBots)
    {
        CloseNetwork();
        _offline = (map, blueBots, redBots);
        byte[] teams = [0, .. Enumerable.Repeat((byte)0, blueBots), .. Enumerable.Repeat((byte)1, redBots)];
        var setup = new GameSetup(_seed, teams) { Map = map };
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
            StartOffline(_offline.Map, _offline.BlueBots, _offline.RedBots);
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
            ShowHostSetup($"Could not host on port {port}: {ex.Message}");
            return;
        }
        _back = status => ShowHostSetup(status);
        Connect("127.0.0.1", _relay.Port, $"Your game on port {_relay.Port}");
    }

    void Join(string address, int port)
    {
        CloseNetwork();
        _back = status => ShowJoin(status);
        Connect(address, port, $"{address}:{port}");
    }

    void Connect(string address, int port, string where)
    {
        _sayDone = false;
        LiteNetClientTransport transport;
        try
        {
            transport = new LiteNetClientTransport(address, port);
        }
        catch (Exception ex)
        {
            _back($"Could not connect: {ex.Message}");
            return;
        }

        var client = new LockstepClient(transport, _settings.PlayerName);
        _client = client;
        client.GameStarted += _ => ShowMatch(new NetworkSession(client, autoPlayLocal: _options.AutoPlay));
        client.Disconnected += reason =>
        {
            // In a match the view shows the error; in the lobby we go back.
            if (_screen is not MatchView)
                _back(client.State == ClientState.Rejected ? $"Rejected: {reason}" : $"Disconnected: {reason}");
        };

        var lobby = new LobbyScreen();
        lobby.Build(client, where);
        lobby.LeaveRequested += () => _back(null);
        ShowScreen(lobby);
    }

    /// <summary>Once in the lobby: the host's map from the setup screen (or map and bots from the command line), and --ready / --say.</summary>
    void ApplyLobbySetup()
    {
        if (_client is not { State: ClientState.Lobby, Lobby: not null })
            return;
        if (!_sayDone && _options.Say is { } say)
        {
            _sayDone = true;
            _client.SendChat(say);
        }
        if (_pendingSetup is not { } setup)
            return;
        _pendingSetup = null;
        if (_client.IsHost)
        {
            if (setup.Map is { } map)
                _client.SetMap(map);
            for (int i = 0; i < Math.Max(setup.BlueBots, setup.RedBots); i++)
            {
                if (i < setup.BlueBots)
                    _client.AddBot(0);
                if (i < setup.RedBots)
                    _client.AddBot(1);
            }
        }
        if (setup.Ready)
            _client.SetReady(true);
    }

    /// <summary>--autostart N: the host starts once N humans are in and everyone is ready (multi-window and headless tests).</summary>
    void AutoStart()
    {
        if (_client is not { State: ClientState.Lobby, IsHost: true, Lobby: { } lobby } || _options.AutoStartPlayers <= 0)
            return;
        int humans = lobby.Slots.Count(s => s.Kind == SlotKind.Human);
        bool everyoneReady = lobby.Slots.Where((s, i) => s.Kind == SlotKind.Human && i != lobby.HostSlot).All(s => s.Ready);
        if (humans >= _options.AutoStartPlayers && everyoneReady)
            _client.StartGame();
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
