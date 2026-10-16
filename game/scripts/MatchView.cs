using System;
using System.IO;
using System.Linq;
using System.Text;
using CastleFight.Net.Sessions;
using CastleFight.Sim;
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

    IMatchSession _session = null!;
    LaunchOptions _options = null!;
    MatchDriver _driver = null!;
    UnitRenderer _renderer = null!;
    CameraRig _cameraRig = null!;
    PlacementGhost _ghost = null!;
    Label _hud = null!;
    Label _banner = null!;
    int _selectedBuilding = -1;
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
    public void Init(IMatchSession session, LaunchOptions options)
    {
        _session = session;
        _options = options;
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

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeConstantOverride("outline_size", 4);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        hudLayer.AddChild(_hud);
        _banner = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.AddThemeFontSizeOverride("font_size", 30);
        _banner.AddThemeConstantOverride("outline_size", 8);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        hudLayer.AddChild(_banner);

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
        _ghost.UpdatePreview(Sim, _session.LocalPlayerId, SelectedTypeId, _cameraRig.ScreenToGround(GetViewport().GetMousePosition()));
        _leaveConfirmTimer = Math.Max(0, _leaveConfirmTimer - delta);

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
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            int buildingIndex = (int)(key.Keycode - Key.Key1);
            if (buildingIndex >= 0 && buildingIndex < Sim.Catalog.BuildableTypeIds.Count)
                _selectedBuilding = buildingIndex;
            else if (key.Keycode == Key.Escape)
                OnEscape();
            else if (key.Keycode == Key.Space && !_session.IsNetworked)
                _driver.Paused = !_driver.Paused;
            else if (key.Keycode == Key.K)
                SpawnStressWave();
            else if (key.Keycode == Key.R && !_session.IsNetworked)
                RestartRequested?.Invoke();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            _selectedBuilding = -1;
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            int typeId = SelectedTypeId;
            if (typeId < 0 || _cameraRig.ScreenToGround(click.Position) is not { } ground)
                return;
            var (tileX, tileY) = PlacementGhost.Snap(ground, Sim.Catalog[typeId].FootprintTiles);
            _session.Issue(Command.PlaceBuilding(_session.LocalPlayerId, (ushort)typeId, tileX, tileY));
        }
    }

    void OnEscape()
    {
        if (_selectedBuilding >= 0)
        {
            _selectedBuilding = -1;
            return;
        }
        // Leaving an online game is final, so ask twice (unless it is already over).
        if (_leaveConfirmTimer > 0 || Sim.IsGameOver || _fatal is not null)
            LeaveRequested?.Invoke();
        else
            _leaveConfirmTimer = LeaveConfirmSeconds;
    }

    int SelectedTypeId => _selectedBuilding >= 0 ? Sim.Catalog.BuildableTypeIds[_selectedBuilding] : -1;

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
        text.Append($"Gold {player.Gold}   Build:");
        var buildable = Sim.Catalog.BuildableTypeIds;
        for (int i = 0; i < buildable.Count; i++)
        {
            var type = Sim.Catalog[buildable[i]];
            string label = $"{i + 1} {type.Name} ({type.Cost})";
            text.Append(i == _selectedBuilding ? $"  [{label}]" : $"   {label} ");
        }
        text.AppendLine();
        string team = player.Team == 0 ? "blue, left" : "red, right";
        text.AppendLine($"1-{buildable.Count} select building, LMB place on the grid in your zones ({team}), RMB/Esc cancel   " +
            $"K +{StressSpawnPerTeam} units   " + (_session.IsNetworked ? "" : "Space pause   R restart   ") + "Esc Esc leave   WASD pan   Wheel zoom");
        if (_notice.Length > 0)
            text.AppendLine(_notice);
        _hud.Text = text.ToString();

        _banner.Text = _fatal
            ?? (Sim.IsGameOver ? (Sim.WinningTeam == player.Team ? "VICTORY" : "DEFEAT") + (_session.IsNetworked ? "\nEsc to leave" : "\nR to restart, Esc to leave")
            : _driver.IsWaiting ? "Waiting for players…"
            : _driver.Paused ? "PAUSED"
            : _leaveConfirmTimer > 0 ? "Press Esc again to leave the match"
            : "");

        if (_options.Smoke && Sim.Tick % (Simulation.TicksPerSecond * 10) < 5)
            GD.Print(text.ToString().Split('\n')[0]);
    }

    public override void _ExitTree() => _session.Dispose();
}
