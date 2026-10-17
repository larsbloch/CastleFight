using System;
using System.IO;
using System.Linq;
using System.Text;
using CastleFight.Game.UI;
using CastleFight.Net.Sessions;
using CastleFight.Sim;
using CastleFight.Sim.FixedMath;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// One match on screen: drives the session through <see cref="MatchDriver"/> (single player or lockstep
/// online, same loop), renders interpolated state, turns input into commands and shows the HUD.
/// </summary>
public partial class MatchView : Node3D
{
    const int StressSpawnPerTeam = 1000;
    const double HudRefreshSeconds = 0.25;
    const double LeaveConfirmSeconds = 2;
    const double ErrorSeconds = 3;
    const float BuilderPickPixels = 32;
    const float BuilderPickTiles = 1.5f;

    IMatchSession _session = null!;
    LaunchOptions _options = null!;
    MatchDriver _driver = null!;
    UnitRenderer _renderer = null!;
    CameraRig _cameraRig = null!;
    PlacementGhost _ghost = null!;
    Settings _settings = null!;
    GameHud _hud = null!;
    BuilderView _builders = null!;
    CommandInfo[] _commands = [];
    bool _builderSelected = true;
    /// <summary>Index into <see cref="UnitCatalog.BuildableTypeIds"/> of the building being placed, or -1.</summary>
    int _placing = -1;
    string _error = "";
    double _errorTimer;
    int _seenBuildFailure = -1;
    bool _pausedBeforeMenu;
    double _hudTimer;
    double _leaveConfirmTimer;
    ulong _lastHash;
    int _hashedTick = -1;
    string _notice = "";
    string? _fatal;

    /// <summary>The player wants to go back to the menu.</summary>
    public event Action? LeaveRequested;

    /// <summary>Single player only: start a new match.</summary>
    public event Action? RestartRequested;

    Simulation Sim => _session.Sim;

    /// <summary>Call before adding to the tree.</summary>
    public void Init(IMatchSession session, LaunchOptions options, Settings settings)
    {
        _session = session;
        _options = options;
        _settings = settings;
        _driver = new MatchDriver(session);
    }

    public override void _Ready()
    {
        var terrain = new TerrainHeights(Sim.Map);
        var scenery = new MapScenery();
        AddChild(scenery);
        scenery.Build(Sim.Map);

        _cameraRig = new CameraRig { Terrain = terrain };
        AddChild(_cameraRig);
        _cameraRig.Frame(new Vector2(Sim.Map.Length.ToFloat(), Sim.Map.Width.ToFloat()));
        _renderer = new UnitRenderer();
        AddChild(_renderer);
        _renderer.Build(Sim, terrain);
        _ghost = new PlacementGhost { Terrain = terrain };
        AddChild(_ghost);
        _builders = new BuilderView { Terrain = terrain };
        AddChild(_builders);
        _builders.Build(Sim);
        _commands = new CommandInfo[Sim.Catalog.BuildableTypeIds.Count];

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _hud = new GameHud { CanRestart = !_session.IsNetworked };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hudLayer.AddChild(_hud);
        _hud.DebugVisible = _settings.ShowDebug;
        _hud.DebugToggled += on =>
        {
            _settings.ShowDebug = on;
            _settings.Save();
        };
        _hud.MenuToggled += OnMenuToggled;
        _hud.LeaveRequested += () => LeaveRequested?.Invoke();
        _hud.RestartRequested += () => RestartRequested?.Invoke();
        _hud.Minimap.SetSimulation(Sim);
        _hud.Minimap.Clicked += _cameraRig.MoveTo;
        _hud.CommandPressed += slot =>
        {
            if (_builderSelected && slot < _commands.Length)
                StartPlacing(slot);
        };

        if (_session is NetworkSession online)
            HookUpNetwork(online);

        if (_options.Smoke)
            SpawnStressWave();
    }

    void HookUpNetwork(NetworkSession online)
    {
        var client = online.Client;
        client.PlayerLeft += player => _notice = $"{PlayerName(player)} left the game. Their buildings keep fighting.";
        client.DesyncDetected += desync => _notice = $"Desync detected at tick {desync.Tick}; stopping at tick {desync.DumpTick} to save a report.";
        client.Disconnected += reason => _fatal = $"Connection lost: {reason}";
        online.DumpRequested += WriteDesyncDump;
        if (_options.PrintHashes)
            online.HashComputed += (tick, hash) => GD.Print($"HASH tick={tick} hash={hash:X16}");
    }

    void WriteDesyncDump(Simulation sim)
    {
        var client = ((NetworkSession)_session).Client;
        string path = ProjectSettings.GlobalizePath($"user://desync_{sim.Tick}_{client.LocalSlot}.txt");
        using (var writer = new StreamWriter(path))
            sim.DumpState(writer);
        _fatal = $"DESYNC - the game was stopped.\nState saved to {path}\nCompare it with the other players' files. Esc to leave.";
        GD.PrintErr($"Desync: state dumped to {path}");
    }

    string PlayerName(int playerId)
    {
        if (_session is NetworkSession { Client: { Start: { } start, Lobby: { } lobby } } && playerId < start.Players.Length)
            return lobby.Slots[start.Players[playerId].Slot].Name;
        return $"Player {playerId + 1}";
    }

    public override void _Process(double delta)
    {
        _driver.Advance(delta);
        _renderer.Draw(Sim, _driver.Alpha);
        _builders.Draw(Sim, _driver.Alpha, _session.LocalPlayerId, _builderSelected, delta);
        var overHud = GetViewport().GuiGetHoveredControl() is not null;
        _ghost.UpdatePreview(Sim, _session.LocalPlayerId, SelectedTypeId, overHud ? null : _cameraRig.ScreenToGround(GetViewport().GetMousePosition()));
        _hud.SetResources(Sim.Players[_session.LocalPlayerId].Gold, wood: 0, food: 0);
        UpdateSelectionHud();
        _leaveConfirmTimer = Math.Max(0, _leaveConfirmTimer - delta);
        _errorTimer = Math.Max(0, _errorTimer - delta);
        _hud.Message = _errorTimer > 0 ? (_notice.Length > 0 ? $"{_notice}\n{_error}" : _error) : _notice;

        if (!_session.IsNetworked && Sim.Tick % Simulation.TicksPerSecond == 0 && Sim.Tick != _hashedTick)
        {
            _hashedTick = Sim.Tick;
            _lastHash = Sim.ComputeStateHash();
        }
        else if (_session is NetworkSession online)
            _lastHash = online.LastHash;

        UpdateHud(delta);

        if (_options.QuitAtTick > 0 && Sim.Tick >= _options.QuitAtTick)
        {
            GD.Print($"Reached tick {Sim.Tick}, state hash {Sim.ComputeStateHash():X16}. Quitting.");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F10 })
            _hud.ToggleMenu();
        else if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
            _hud.DebugVisible = !_hud.DebugVisible;
        else if (_hud.MenuOpen)
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
                _hud.ToggleMenu();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            int buildingIndex = (int)(key.Keycode - Key.Key1);
            if (buildingIndex >= 0 && buildingIndex < _commands.Length)
            {
                if (_builderSelected)
                    StartPlacing(buildingIndex);
            }
            else if (key.Keycode == Key.Escape)
                OnEscape();
            else if (key.Keycode == Key.Space && !_session.IsNetworked)
                _driver.Paused = !_driver.Paused;
            else if (key.Keycode == Key.K)
                SpawnStressWave();
            else if (key.Keycode == Key.R && !_session.IsNetworked)
                RestartRequested?.Invoke();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rightClick)
        {
            // Right click cancels placing; otherwise it sends the selected builder there.
            if (_placing >= 0)
                _placing = -1;
            else if (_builderSelected && _cameraRig.ScreenToGround(rightClick.Position) is { } ground)
                _session.Issue(Command.MoveBuilder(_session.LocalPlayerId, new FixVec2(Fix.FromFloat(ground.X), Fix.FromFloat(ground.Z))));
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            // Left click places the building being placed; otherwise it selects the builder if it hits it, else deselects.
            if (_placing >= 0)
                TryPlace(click.Position);
            else
                _builderSelected = IsOnLocalBuilder(click.Position);
        }
    }

    void StartPlacing(int buildingIndex)
    {
        _placing = buildingIndex;
        var type = Sim.Catalog[SelectedTypeId];
        if (Sim.Players[_session.LocalPlayerId].Gold < type.Cost)
            ShowError($"Not enough gold for the {type.Name} ({type.Cost}).");
    }

    void TryPlace(Vector2 screenPosition)
    {
        int typeId = SelectedTypeId;
        if (_cameraRig.ScreenToGround(screenPosition) is not { } ground)
            return;
        var (tileX, tileY) = PlacementGhost.Snap(ground, Sim.Catalog[typeId].FootprintTiles);
        var result = Sim.CheckPlacement(_session.LocalPlayerId, typeId, tileX, tileY);
        if (result != PlacementResult.Ok)
        {
            ShowError(PlacementError(result));
            return;
        }
        _session.Issue(Command.Build(_session.LocalPlayerId, (ushort)typeId, tileX, tileY));
        _placing = -1;
    }

    static string PlacementError(PlacementResult result) => result switch
    {
        PlacementResult.NotEnoughGold => "Not enough gold.",
        PlacementResult.OutsideZone => "Can't build there: outside your build zones.",
        PlacementResult.Occupied => "Can't build there: something is in the way.",
        PlacementResult.OnRamp => "Can't build on a ramp.",
        _ => "Can't build there.",
    };

    void ShowError(string text)
    {
        _error = text;
        _errorTimer = ErrorSeconds;
    }

    /// <summary>Whether a click at <paramref name="screenPosition"/> hits the local builder: near the star on screen, or near it on the ground.</summary>
    bool IsOnLocalBuilder(Vector2 screenPosition)
    {
        var builder = Sim.Players[_session.LocalPlayerId].Builder;
        var star = _builders.WorldPosition(builder, _driver.Alpha);
        var camera = _cameraRig.Camera;
        if (!camera.IsPositionBehind(star) && camera.UnprojectPosition(star).DistanceTo(screenPosition) < BuilderPickPixels)
            return true;
        return _cameraRig.ScreenToGround(screenPosition) is { } ground
            && new Vector2(ground.X - star.X, ground.Z - star.Z).Length() < BuilderPickTiles;
    }

    /// <summary>The command HUD shows the buildings while the builder is selected; the stats well says what it is doing.</summary>
    void UpdateSelectionHud()
    {
        var player = Sim.Players[_session.LocalPlayerId];
        var builder = player.Builder;
        if (builder.BuildFailedTick != _seenBuildFailure)
        {
            // The builder reached the site but could not build (someone built there first, or gold ran out).
            ShowError(PlacementError(builder.BuildFailure));
            _seenBuildFailure = builder.BuildFailedTick;
        }

        if (!_builderSelected)
        {
            _placing = -1;
            _hud.SetCommands([]);
            _hud.SetSelection(null, "", null);
            return;
        }

        var buildable = Sim.Catalog.BuildableTypeIds;
        for (int i = 0; i < _commands.Length; i++)
        {
            var type = Sim.Catalog[buildable[i]];
            _commands[i] = new CommandInfo(type.Name, type.Cost, (i + 1).ToString(), player.Gold >= type.Cost, Active: i == _placing);
        }
        _hud.SetCommands(_commands);

        string site = builder.BuildTypeId >= 0 ? Sim.Catalog[builder.BuildTypeId].Name : "";
        string status = builder.Order switch
        {
            BuilderOrder.Move => "Walking.",
            BuilderOrder.GoToSite => $"Going to build: {site}.",
            BuilderOrder.Construct => $"Building {site}: {builder.BuildProgress * 100 / Simulation.BuildTicks}%. Moving away cancels it.",
            _ => _placing >= 0 ? $"Left-click to place the {Sim.Catalog[SelectedTypeId].Name}, right-click to cancel." : "Idle. Right-click to move, pick a building to build.",
        };
        _hud.SetSelection("Builder", status, UnitRenderer.TeamColors[player.Team].Lightened(0.35f));
    }

    void OnEscape()
    {
        if (_placing >= 0)
        {
            _placing = -1;
            return;
        }
        if (_builderSelected)
        {
            _builderSelected = false;
            return;
        }
        // Leaving an online game is final, so ask twice (unless it is already over).
        if (_leaveConfirmTimer > 0 || Sim.IsGameOver || _fatal is not null)
            LeaveRequested?.Invoke();
        else
            _leaveConfirmTimer = LeaveConfirmSeconds;
    }

    /// <summary>The menu pauses a single player game, like Warcraft 3's; online the game goes on.</summary>
    void OnMenuToggled(bool open)
    {
        if (_session.IsNetworked)
            return;
        if (open)
        {
            _pausedBeforeMenu = _driver.Paused;
            _driver.Paused = true;
        }
        else
            _driver.Paused = _pausedBeforeMenu;
    }

    int SelectedTypeId => _placing >= 0 ? Sim.Catalog.BuildableTypeIds[_placing] : -1;

    /// <summary>
    /// K: a big wave for stress tests. Offline it spawns for one player per team; online only for yourself
    /// (the relay drops commands for other players).
    /// </summary>
    void SpawnStressWave()
    {
        var mobileTypes = Sim.Catalog.MobileTypeIds;
        var playerIds = _session.IsNetworked
            ? [_session.LocalPlayerId]
            : Enumerable.Range(0, Simulation.TeamCount)
                .Select(team => Sim.Players.FirstOrDefault(p => p.Team == team)?.Id ?? -1)
                .Where(id => id >= 0)
                .ToArray();
        foreach (int playerId in playerIds)
        {
            foreach (int typeId in mobileTypes)
                _session.Issue(Command.DebugSpawn(playerId, (ushort)typeId, StressSpawnPerTeam / mobileTypes.Count));
        }
    }

    void UpdateHud(double delta)
    {
        _hudTimer -= delta;
        if (_hudTimer > 0)
            return;
        _hudTimer = HudRefreshSeconds;

        var text = new StringBuilder();
        text.AppendLine($"FPS {Engine.GetFramesPerSecond():0}   Units {Sim.Units.AliveCount:N0}   Tick {Sim.Tick}   " +
            $"Sim step {_driver.StepMsAverage:0.00} ms avg / {_driver.StepMsWorst:0.00} ms worst   State hash {_lastHash:X16}");
        _driver.ResetWorst();

        if (_session is NetworkSession online)
        {
            text.AppendLine($"Online, {Sim.Map.Name}   " +
                $"Ping {online.Client.RoundTripMs} ms   Input delay {_session.InputDelayTicks} ticks ({_session.InputDelayTicks * 1000 / Simulation.TicksPerSecond} ms)   " +
                $"Buffered {_session.BufferedTicks}");
        }

        var player = Sim.Players[_session.LocalPlayerId];
        string team = player.Team == 0 ? "blue, left" : "red, right";
        text.AppendLine($"Builder ({team}): LMB select / deselect, RMB move, 1-{_commands.Length} or the command HUD to build, then LMB place, RMB/Esc cancel");
        text.AppendLine($"K +{StressSpawnPerTeam} units   " + (_session.IsNetworked ? "" : "Space pause   R restart   ") + "Esc Esc leave   WASD pan   Wheel zoom   F10 menu   F11 debug");
        _hud.DebugText = text.ToString();

        _hud.Banner = _fatal
            ?? (_hud.MenuOpen ? ""
            : Sim.IsGameOver ? (Sim.WinningTeam == player.Team ? "VICTORY" : "DEFEAT") + (_session.IsNetworked ? "\nEsc to leave" : "\nR to restart, Esc to leave")
            : _driver.IsWaiting ? "Waiting for players…"
            : _driver.Paused ? "PAUSED"
            : _leaveConfirmTimer > 0 ? "Press Esc again to leave the match"
            : "");

        if (_options.Smoke && Sim.Tick % (Simulation.TicksPerSecond * 10) < 5)
            GD.Print(text.ToString().Split('\n')[0]);
    }

    public override void _ExitTree() => _session.Dispose();
}
