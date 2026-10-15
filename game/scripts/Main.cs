using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CastleFight.Sim;
using CastleFight.Sim.Bots;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Composition root. Runs the simulation at a fixed tick rate, feeds it commands the same way a
/// lockstep network layer will (scheduled a few ticks ahead), and renders interpolated state.
/// </summary>
public partial class Main : Node3D
{
    const int LocalPlayerId = 0;
    const int PlayersPerTeam = 3;
    const int InputDelayTicks = 3;
    const int MaxStepsPerFrame = 5;
    const int StressSpawnPerTeam = 1000;
    const double TickSeconds = 1.0 / Simulation.TicksPerSecond;
    const double HudRefreshSeconds = 0.25;

    readonly Stopwatch _stepTimer = new();
    Simulation _sim = null!;
    CommandSchedule _schedule = null!;
    ScriptedBot[] _bots = [];
    UnitRenderer _renderer = null!;
    CameraRig _cameraRig = null!;
    Label _hud = null!;
    ulong _seed = 1;
    int _selectedBuilding = -1;
    MapId _map = MapId.SingleLane;
    PlacementGhost _ghost = null!;
    bool _paused;
    double _accumulator;
    double _hudTimer;
    double _stepMsAverage;
    double _stepMsWorst;
    ulong _lastHash;
    bool _smokeTest;

    public override void _Ready()
    {
        _cameraRig = new CameraRig();
        AddChild(_cameraRig);
        _renderer = new UnitRenderer();
        AddChild(_renderer);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeConstantOverride("outline_size", 4);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        var hudLayer = new CanvasLayer();
        hudLayer.AddChild(_hud);
        AddChild(hudLayer);

        _ghost = new PlacementGhost();
        AddChild(_ghost);

        // Headless smoke test: godot --headless --fixed-fps 60 --quit-after 1200 -- --smoke [--map 0|1|2]
        var args = OS.GetCmdlineUserArgs();
        int mapArg = Array.IndexOf(args, "--map");
        if (mapArg >= 0 && mapArg + 1 < args.Length && byte.TryParse(args[mapArg + 1], out byte mapId) && MapCatalog.IsValid((MapId)mapId))
            _map = (MapId)mapId;

        StartMatch();
        var scenery = new MapScenery();
        AddChild(scenery);
        scenery.Build(_sim.Map);
        _cameraRig.Frame(new Vector2(_sim.Map.Length.ToFloat(), _sim.Map.Width.ToFloat()));

        _smokeTest = args.Contains("--smoke");
        if (_smokeTest)
            SpawnStressWave();
    }

    void StartMatch()
    {
        var setup = GameSetup.ForTeams(_seed, PlayersPerTeam, _map);
        _sim = new Simulation(setup);
        _schedule = new CommandSchedule();
        _bots = Enumerable.Range(0, setup.PlayerTeams.Count)
            .Where(id => id != LocalPlayerId)
            .Select(id => new ScriptedBot(id, _seed))
            .ToArray();
        _renderer.Build(_sim);
        _accumulator = 0;
        _stepMsAverage = 0;
        _stepMsWorst = 0;
        _lastHash = 0;
    }

    public override void _Process(double delta)
    {
        if (!_paused)
        {
            _accumulator += delta;
            int steps = 0;
            while (_accumulator >= TickSeconds && steps < MaxStepsPerFrame)
            {
                StepSimulation();
                _accumulator -= TickSeconds;
                steps++;
            }
            // Can't keep up: drop the backlog rather than spiral. In multiplayer this is where
            // the game would slow down for everyone instead.
            _accumulator = Math.Min(_accumulator, TickSeconds);
        }

        _renderer.Draw(_sim, (float)Math.Clamp(_accumulator / TickSeconds, 0, 1));
        _ghost.UpdatePreview(_sim, LocalPlayerId, SelectedTypeId, _cameraRig.ScreenToGround(GetViewport().GetMousePosition()));
        UpdateHud(delta);
    }

    void StepSimulation()
    {
        foreach (var bot in _bots)
        {
            if (bot.TryThink(_sim, out var command))
                _schedule.Add(_sim.Tick + InputDelayTicks, command);
        }

        var commands = _schedule.TakeForTick(_sim.Tick);
        _stepTimer.Restart();
        _sim.Step(commands);
        double ms = _stepTimer.Elapsed.TotalMilliseconds;
        _stepMsAverage = _stepMsAverage == 0 ? ms : _stepMsAverage * 0.95 + ms * 0.05;
        _stepMsWorst = Math.Max(_stepMsWorst, ms);

        if (_sim.Tick % Simulation.TicksPerSecond == 0)
            _lastHash = _sim.ComputeStateHash();
    }

    /// <summary>Local input becomes a command scheduled a few ticks ahead, exactly as it will over the network.</summary>
    void Issue(Command command) => _schedule.Add(_sim.Tick + InputDelayTicks, command);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            int buildingIndex = (int)(key.Keycode - Key.Key1);
            if (buildingIndex >= 0 && buildingIndex < _sim.Catalog.BuildableTypeIds.Count)
                _selectedBuilding = buildingIndex;
            else if (key.Keycode == Key.Escape)
                _selectedBuilding = -1;
            else if (key.Keycode == Key.Space)
                _paused = !_paused;
            else if (key.Keycode == Key.K)
                SpawnStressWave();
            else if (key.Keycode == Key.R)
            {
                _seed++;
                StartMatch();
            }
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
            var (tileX, tileY) = PlacementGhost.Snap(ground, _sim.Catalog[typeId].FootprintTiles);
            Issue(Command.PlaceBuilding(LocalPlayerId, (ushort)typeId, tileX, tileY));
        }
    }

    int SelectedTypeId => _selectedBuilding >= 0 ? _sim.Catalog.BuildableTypeIds[_selectedBuilding] : -1;

    void SpawnStressWave()
    {
        var mobileTypes = _sim.Catalog.MobileTypeIds;
        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            int playerId = team * PlayersPerTeam;
            foreach (int typeId in mobileTypes)
                Issue(Command.DebugSpawn(playerId, (ushort)typeId, StressSpawnPerTeam / mobileTypes.Count));
        }
    }

    void UpdateHud(double delta)
    {
        _hudTimer -= delta;
        if (_hudTimer > 0)
            return;
        _hudTimer = HudRefreshSeconds;

        var text = new StringBuilder();
        text.AppendLine($"FPS {Engine.GetFramesPerSecond():0}   Units {_sim.Units.AliveCount:N0}   Tick {_sim.Tick}   " +
            $"Sim step {_stepMsAverage:0.00} ms avg / {_stepMsWorst:0.00} ms worst   State hash {_lastHash:X16}");
        _stepMsWorst = 0;

        text.Append($"Gold {_sim.Players[LocalPlayerId].Gold}   Build:");
        var buildable = _sim.Catalog.BuildableTypeIds;
        for (int i = 0; i < buildable.Count; i++)
        {
            var type = _sim.Catalog[buildable[i]];
            string label = $"{i + 1} {type.Name} ({type.Cost})";
            text.Append(i == _selectedBuilding ? $"  [{label}]" : $"   {label} ");
        }
        text.AppendLine();
        text.AppendLine($"1-{buildable.Count} select building, LMB place on the grid in your (blue) zone, RMB/Esc cancel   K +{StressSpawnPerTeam} units per team   Space pause   R restart   WASD pan   Wheel zoom");

        if (_paused)
            text.AppendLine("PAUSED");
        if (_sim.IsGameOver)
            text.AppendLine(_sim.WinningTeam == _sim.Players[LocalPlayerId].Team ? "VICTORY - press R" : "DEFEAT - press R");
        _hud.Text = text.ToString();
        if (_smokeTest && _sim.Tick % (Simulation.TicksPerSecond * 10) < 5)
            GD.Print(text.ToString().Split('\n')[0]);
    }
}
