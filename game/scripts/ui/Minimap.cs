using System;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// The map in the bottom-left corner: the terrain (a <see cref="MapIcon"/> letterboxed to the map's aspect), a dot
/// per unit and a square per building in team colours. Click or drag to move the camera.
/// </summary>
public partial class Minimap : Control
{
    const double RefreshSeconds = 0.1;
    const float DotSize = 2.5f;

    /// <summary>Saturated team colours that stand out on the dark terrain.</summary>
    static readonly Color[] DotColors = [new(0.2f, 0.45f, 1f), new(1f, 0.16f, 0.12f)];

    readonly MapIcon _terrain = new();
    readonly Control _units = new() { MouseFilter = MouseFilterEnum.Ignore };
    Simulation? _sim;
    Rect2 _area;
    double _refreshTimer;

    // One stamp per dot-sized cell and team, so a dense crowd costs one rectangle per cell, not one per unit.
    int[][] _stamps = [[], []];
    int _stampColumns;
    int _frame;

    /// <summary>The player clicked or dragged on the map; the point is in map units.</summary>
    public event Action<Vector2>? Clicked;

    public override void _Ready()
    {
        ClipContents = true;
        AddChild(_terrain);
        AddChild(_units);
        _units.Draw += DrawUnits;
        Resized += Layout;
    }

    public void SetSimulation(Simulation sim)
    {
        _sim = sim;
        _terrain.Definition = sim.Map;
        Layout();
    }

    void Layout()
    {
        if (_sim is null)
            return;
        // Letterbox: the widest box with the map's aspect that fits, centred.
        float aspect = _sim.Map.LengthTiles / (float)_sim.Map.WidthTiles;
        var size = Size.X / Size.Y > aspect ? new Vector2(Size.Y * aspect, Size.Y) : new Vector2(Size.X, Size.X / aspect);
        var box = new Rect2((Size - size) / 2, size);
        _units.Size = Size;
        _terrain.Position = box.Position;
        _terrain.Size = box.Size;
        _area = box.Grow(-2); // MapIcon draws the map inset by 2

        _stampColumns = (int)(Size.X / DotSize) + 1;
        int cells = _stampColumns * ((int)(Size.Y / DotSize) + 1);
        _stamps = [new int[cells], new int[cells]];
        _units.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _refreshTimer -= delta;
        if (_refreshTimer > 0)
            return;
        _refreshTimer = RefreshSeconds;
        _units.QueueRedraw();
    }

    Vector2 MapScale => _sim is null ? Vector2.One : _area.Size / new Vector2(_sim.Map.LengthTiles, _sim.Map.WidthTiles);

    Vector2 ToMinimap(Vector2 mapPoint) => _area.Position + mapPoint * MapScale;

    void DrawUnits()
    {
        if (_sim is null)
            return;
        var u = _sim.Units;
        var scale = MapScale;
        _frame++;
        for (int i = 0; i < u.HighWater; i++)
        {
            if (!u.Alive[i])
                continue;
            var type = _sim.Catalog[u.Type[i]];
            if (type.IsCastle)
                continue; // the map icon draws the castles
            int team = u.Team[i];
            var at = ToMinimap(new Vector2(u.Pos[i].X.ToFloat(), u.Pos[i].Y.ToFloat()));
            if (type.IsStructure)
            {
                var size = Vector2.One * Math.Max(3.5f, type.FootprintTiles * scale.X);
                var rect = new Rect2(at - size / 2, size);
                _units.DrawRect(rect.Grow(1), new Color(0, 0, 0, 0.8f));
                _units.DrawRect(rect, DotColors[team] * 0.8f);
                continue;
            }
            int cell = (int)(at.Y / DotSize) * _stampColumns + (int)(at.X / DotSize);
            if ((uint)cell >= (uint)_stamps[team].Length || _stamps[team][cell] == _frame)
                continue;
            _stamps[team][cell] = _frame;
            _units.DrawRect(new Rect2(at - Vector2.One * (DotSize / 2), Vector2.One * DotSize), DotColors[team]);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        bool drag = @event is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left);
        bool click = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
        if (_sim is null || !(drag || click))
            return;
        var point = (((InputEventMouse)@event).Position - _area.Position) / MapScale;
        Clicked?.Invoke(point.Clamp(Vector2.Zero, new Vector2(_sim.Map.LengthTiles, _sim.Map.WidthTiles)));
        AcceptEvent();
    }
}
