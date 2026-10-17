using System;
using System.Collections.Generic;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// The in-match HUD in the style of Warcraft 3. A stone top bar with the Menu and Debug buttons, a medallion and
/// gold / wood / food on the right. At the bottom a castle wall of stone: a tower with the minimap on the left, the
/// portrait and unit stats in the middle, and a tower with the inventory and the command HUD (4x3 slots) on the right.
/// The stone is drawn here; only the stone (and the controls on it) catch the mouse, the rest falls through to the
/// game. Laid out in menu units (the window is scaled so the HUD is at least 1600x900, see <c>App</c>).
/// </summary>
public partial class GameHud : Control
{
    const float TopBarHeight = 34;
    const float TowerHeight = 206;
    const float CommandTowerHeight = 260; // the right tower is taller to fit the command HUD
    const float WallHeight = 170;
    const float MerlonWidth = 40, MerlonHeight = 20, MerlonGap = 20;
    const float Pad = 16;
    const float StoneTileSize = 320; // = the stone texture size, so it maps 1:1

    const float MinimapWidth = 316, MinimapHeight = 182;
    const float PortraitWidth = 150, PortraitArch = 34;
    const int InventoryColumns = 2, InventoryRows = 3;
    const float InventorySlot = 46, InventoryGap = 5;
    const int CommandColumns = 4, CommandRows = 3;
    const float CommandSlot = 64, CommandGap = 6;
    const float SlotInset = 6; // from a well's edge to its slots

    static readonly Color Bronze = new(0.7f, 0.52f, 0.24f);
    static readonly Color Edge = new(0.03f, 0.025f, 0.02f);

    static ImageTexture? _stone;

    readonly Button _menuButton = new() { Text = "Menu (F10)", ThemeTypeVariation = "HudButton", FocusMode = FocusModeEnum.None };
    readonly Button _debugButton = new() { Text = "Debug (F11)", ThemeTypeVariation = "HudButton", FocusMode = FocusModeEnum.None, ToggleMode = true };
    readonly Label[] _resources = new Label[3];
    readonly HBoxContainer _resourceBar = new();
    readonly Label _debug = new() { ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _message = new() { ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Bottom };
    readonly Label _banner = new()
    {
        ThemeTypeVariation = "HeaderLabel", MouseFilter = MouseFilterEnum.Ignore,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    readonly PanelContainer _minimapWell = new() { ThemeTypeVariation = "HudWell" };
    readonly PanelContainer _statsWell = new() { ThemeTypeVariation = "HudWell" };
    readonly PanelContainer _inventoryWell = new() { ThemeTypeVariation = "HudWell" };
    readonly PanelContainer _commandWell = new() { ThemeTypeVariation = "HudWell" };
    readonly Label _inventoryLabel = new() { Text = "Inventory", ThemeTypeVariation = "HudPlaqueLabel", HorizontalAlignment = HorizontalAlignment.Center };
    readonly List<PanelContainer> _inventorySlots = new();
    readonly List<PanelContainer> _commandSlots = new();
    readonly List<CommandButton> _commandButtons = new();
    readonly Label _selectionName = new() { ThemeTypeVariation = "HudPlaqueLabel", MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _selectionStatus = new() { ThemeTypeVariation = "HudDebugLabel", MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    Color? _portraitStar;
    readonly Control _menu = new() { Visible = false };

    // Stone shapes from the last layout, for drawing and for deciding which clicks the HUD takes.
    Rect2 _topBar, _leftTower, _rightTower, _wall, _portrait, _portraitSurround;
    Vector2 _medallion;
    const float MedallionRadius = 36;

    public Minimap Minimap { get; } = new();

    /// <summary>Show "Restart" in the menu (single player).</summary>
    public bool CanRestart { get; init; }

    public event Action<bool>? MenuToggled;
    public event Action<bool>? DebugToggled;
    public event Action? LeaveRequested;
    public event Action? RestartRequested;

    /// <summary>A command HUD button was clicked; the argument is the slot (0..11, row by row).</summary>
    public event Action<int>? CommandPressed;

    public bool MenuOpen => _menu.Visible;

    public bool DebugVisible
    {
        get => _debug.Visible;
        set
        {
            if (_debug.Visible == value)
                return;
            _debug.Visible = value;
            _debugButton.SetPressedNoSignal(value);
            DebugToggled?.Invoke(value);
        }
    }

    // Godot breaks lines on '\r' as well, so Windows line endings would double-space.
    public string DebugText { set => _debug.Text = value.ReplaceLineEndings("\n"); }
    public string Message { set => _message.Text = value.ReplaceLineEndings("\n"); }
    public string Banner { set => _banner.Text = value; }

    public override void _Ready()
    {
        Theme = UiTheme.Shared;
        TextureRepeat = TextureRepeatEnum.Enabled; // the stone tiles across every shape
        _stone ??= UiArt.StoneTile((int)StoneTileSize);

        AddChild(_banner);
        AddChild(_debug);
        AddChild(_message);
        // Below the top bar and the debug readout, so the middle of the view (e.g. the hole at the end) stays clear.
        _banner.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _banner.OffsetTop = 150;
        _banner.OffsetBottom = 260;
        _banner.AddThemeFontSizeOverride("font_size", 34);

        AddChild(_menuButton);
        AddChild(_debugButton);
        _menuButton.Pressed += ToggleMenu;
        _debugButton.Toggled += on => DebugVisible = on;
        _debugButton.SetPressedNoSignal(_debug.Visible);

        AddChild(_resourceBar);
        _resourceBar.AddThemeConstantOverride("separation", 8);
        foreach (var kind in new[] { ResourceKind.Gold, ResourceKind.Wood, ResourceKind.Food })
        {
            var box = new PanelContainer { ThemeTypeVariation = "HudResource", CustomMinimumSize = new Vector2(160, 0) };
            var row = new HBoxContainer();
            row.AddChild(new ResourceIcon { Kind = kind, CustomMinimumSize = new Vector2(22, 22), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            var value = new Label { Text = "0", ThemeTypeVariation = "HudValueLabel", HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(value);
            box.AddChild(row);
            _resourceBar.AddChild(box);
            _resources[(int)kind] = value;
        }

        AddChild(_minimapWell);
        AddChild(Minimap);
        AddChild(_statsWell);
        var selection = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        selection.AddChild(_selectionName);
        selection.AddChild(_selectionStatus);
        _statsWell.AddChild(selection);
        AddChild(_inventoryWell);
        AddChild(_inventoryLabel);
        AddChild(_commandWell);
        for (int i = 0; i < InventoryColumns * InventoryRows; i++)
            _inventorySlots.Add(AddSlot());
        for (int i = 0; i < CommandColumns * CommandRows; i++)
        {
            _commandSlots.Add(AddSlot());
            var button = new CommandButton();
            int slot = i;
            button.Pressed += () => CommandPressed?.Invoke(slot);
            AddChild(button);
            _commandButtons.Add(button);
        }

        BuildMenu();
        Resized += Layout;
        Layout();
    }

    PanelContainer AddSlot()
    {
        var slot = new PanelContainer { ThemeTypeVariation = "HudSlot" };
        AddChild(slot);
        return slot;
    }

    void BuildMenu()
    {
        AddChild(_menu);
        _menu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) }; // also keeps clicks off the game
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menu.AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menu.AddChild(center);
        var board = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        center.AddChild(board);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        board.AddChild(column);
        column.AddChild(new Label { Text = "Menu", ThemeTypeVariation = "HeaderLabel", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(MenuButton("Return to Game", ToggleMenu));
        if (CanRestart)
            column.AddChild(MenuButton("Restart", () => RestartRequested?.Invoke()));
        column.AddChild(MenuButton("Leave Match", () => LeaveRequested?.Invoke()));
    }

    static Button MenuButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 46) };
        button.Pressed += pressed;
        return button;
    }

    public void ToggleMenu()
    {
        _menu.Visible = !_menu.Visible;
        MenuToggled?.Invoke(_menu.Visible);
    }

    public void SetResources(int gold, int wood, int food)
    {
        SetValue(ResourceKind.Gold, gold);
        SetValue(ResourceKind.Wood, wood);
        SetValue(ResourceKind.Food, food);
    }

    /// <summary>Fills the command HUD from slot 0 (top left), row by row; the remaining slots stay empty.</summary>
    public void SetCommands(ReadOnlySpan<CommandInfo> commands)
    {
        for (int i = 0; i < _commandButtons.Count; i++)
            _commandButtons[i].SetCommand(i < commands.Length ? commands[i] : null);
    }

    /// <summary>What is selected: a name and a status line in the stats well, and a star in the portrait. Null clears it.</summary>
    public void SetSelection(string? name, string status, Color? portraitStar)
    {
        if (_selectionName.Text != (name ?? ""))
            _selectionName.Text = name ?? "";
        if (_selectionStatus.Text != status)
            _selectionStatus.Text = status;
        if (_portraitStar != portraitStar)
        {
            _portraitStar = portraitStar;
            QueueRedraw();
        }
    }

    void SetValue(ResourceKind kind, int value)
    {
        string text = value.ToString();
        if (_resources[(int)kind].Text != text)
            _resources[(int)kind].Text = text;
    }

    // ---- Layout ----

    void Layout()
    {
        float w = Size.X, h = Size.Y;

        // Top bar
        _topBar = new Rect2(0, 0, w, TopBarHeight);
        _medallion = new Vector2(w / 2, 20);
        Place(_menuButton, new Rect2(8, 4, 150, TopBarHeight - 8));
        Place(_debugButton, new Rect2(164, 4, 150, TopBarHeight - 8));
        var resourceSize = _resourceBar.GetCombinedMinimumSize();
        Place(_resourceBar, new Rect2(w - 8 - resourceSize.X, 4, resourceSize.X, TopBarHeight - 8));
        Place(_debug, new Rect2(12, TopBarHeight + 10, w - 24, 200));

        // Left tower: the minimap.
        _leftTower = new Rect2(0, h - TowerHeight, Pad + MinimapWidth + Pad, TowerHeight);
        var minimap = new Rect2(Pad, h - 12 - MinimapHeight, MinimapWidth, MinimapHeight);
        Place(_minimapWell, minimap);
        Place(Minimap, minimap.Grow(-UiArt.WellMargin + 2));
        Place(_message, new Rect2(12, h - TowerHeight - MerlonHeight - 110, 900, 100));

        // Right tower: inventory and the command HUD.
        var commandSize = SlotGrid(CommandColumns, CommandRows, CommandSlot, CommandGap);
        var inventorySize = SlotGrid(InventoryColumns, InventoryRows, InventorySlot, InventoryGap);
        float rightWidth = Pad + inventorySize.X + 12 + commandSize.X + Pad;
        _rightTower = new Rect2(w - rightWidth, h - CommandTowerHeight, rightWidth, CommandTowerHeight);
        var commands = new Rect2(w - Pad - commandSize.X, h - 12 - commandSize.Y, commandSize);
        PlaceSlots(_commandWell, _commandSlots, commands, CommandColumns, CommandSlot, CommandGap);
        for (int i = 0; i < _commandButtons.Count; i++)
            Place(_commandButtons[i], new Rect2(_commandSlots[i].Position, _commandSlots[i].Size));
        var inventory = new Rect2(commands.Position.X - 12 - inventorySize.X, h - 12 - inventorySize.Y, inventorySize);
        PlaceSlots(_inventoryWell, _inventorySlots, inventory, InventoryColumns, InventorySlot, InventoryGap);
        Place(_inventoryLabel, new Rect2(inventory.Position.X - 10, inventory.Position.Y - 24, inventory.Size.X + 20, 22));

        // The wall between: portrait and unit stats.
        _wall = new Rect2(_leftTower.End.X, h - WallHeight, _rightTower.Position.X - _leftTower.End.X, WallHeight);
        _portrait = new Rect2(_wall.Position.X + 22, h - TowerHeight + 6, PortraitWidth, TowerHeight - 18);
        _portraitSurround = _portrait.Grow(12);
        float statsX = _portrait.End.X + 18;
        Place(_statsWell, new Rect2(statsX, h - WallHeight + 24, _wall.End.X - 16 - statsX, WallHeight - 36));

        QueueRedraw();
    }

    static Vector2 SlotGrid(int columns, int rows, float slot, float gap) =>
        new Vector2(columns * slot + (columns - 1) * gap, rows * slot + (rows - 1) * gap) + Vector2.One * 2 * SlotInset;

    static void PlaceSlots(Control well, List<PanelContainer> slots, Rect2 area, int columns, float slot, float gap)
    {
        Place(well, area);
        for (int i = 0; i < slots.Count; i++)
        {
            var at = area.Position + Vector2.One * SlotInset + new Vector2(i % columns, i / columns) * (slot + gap);
            Place(slots[i], new Rect2(at, slot, slot));
        }
    }

    static void Place(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    /// <summary>Only the stone takes clicks; everything else is the game.</summary>
    public override bool _HasPoint(Vector2 point)
    {
        if (_topBar.HasPoint(point) || point.DistanceTo(_medallion) < MedallionRadius || _portraitSurround.HasPoint(point))
            return true;
        foreach (var part in new[] { _leftTower, _rightTower, _wall })
        {
            if (part.GrowSide(Side.Top, MerlonHeight).HasPoint(point))
                return true;
        }
        return false;
    }

    // ---- Drawing ----

    public override void _Draw()
    {
        // Soft shadows the stone casts onto the game view.
        Shadow(new Rect2(0, TopBarHeight, Size.X, 14), down: true);
        foreach (var part in new[] { _leftTower, _wall, _rightTower })
            Shadow(new Rect2(part.Position.X, part.Position.Y - MerlonHeight - 10, part.Size.X, 10), down: false);

        DrawWallSection(_wall, skip: _portraitSurround);
        // The towers stand in front of the wall and throw a shadow on it.
        DrawRect(new Rect2(_wall.Position.X, _wall.Position.Y, 10, _wall.Size.Y), new Color(0, 0, 0, 0.35f));
        DrawRect(new Rect2(_wall.End.X - 10, _wall.Position.Y, 10, _wall.Size.Y), new Color(0, 0, 0, 0.35f));
        DrawWallSection(_leftTower, skip: null);
        DrawWallSection(_rightTower, skip: null);
        DrawPortrait();
        DrawTopBar();
    }

    void DrawWallSection(Rect2 r, Rect2? skip)
    {
        Stone(r, 1f);
        // A ledge along the top, then the battlements on it.
        Stone(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 8), 1.3f);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 8, r.Size.X, 2), Edge with { A = 0.8f });
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 10, r.Size.X, 6), new Color(0, 0, 0, 0.25f));

        int count = Math.Max(1, (int)((r.Size.X - MerlonGap) / (MerlonWidth + MerlonGap)));
        float spacing = (r.Size.X - count * MerlonWidth) / (count + 1);
        for (int i = 0; i < count; i++)
        {
            var merlon = new Rect2(r.Position.X + spacing + i * (MerlonWidth + spacing), r.Position.Y - MerlonHeight, MerlonWidth, MerlonHeight + 1);
            if (skip is { } hole && hole.Grow(4).Intersects(merlon))
                continue;
            Stone(merlon, 1.15f);
            Bevel(merlon);
        }
        Bevel(r);
    }

    void DrawPortrait()
    {
        // A stone arch rising out of the wall, with a bronze-rimmed dark recess for the portrait.
        var surround = Arch(_portraitSurround, PortraitArch + 12);
        DrawPolygon(surround, new[] { new Color(1.2f, 1.2f, 1.2f) }, Uvs(surround), _stone);
        DrawPolyline([.. surround, surround[0]], Edge, 2, true);

        var well = Arch(_portrait, PortraitArch);
        DrawColoredPolygon(well, new Color(0.03f, 0.03f, 0.036f));
        var inner = Arch(_portrait.Grow(-6), PortraitArch - 6);
        DrawPolyline([.. inner, inner[0]], new Color(0, 0, 0, 0.6f), 6, true); // inner shadow
        DrawPolyline([.. well, well[0]], Bronze, 4, true);
        var rim = Arch(_portrait.Grow(-2.5f), PortraitArch - 2.5f);
        DrawPolyline([.. rim, rim[0]], Edge, 1.2f, true);

        if (_portraitStar is { } color)
        {
            var star = Star(_portrait.GetCenter() + new Vector2(0, 8), 52, 22);
            DrawColoredPolygon(star, color);
            DrawPolyline([.. star, star[0]], Edge, 2, true);
        }
    }

    static Vector2[] Star(Vector2 center, float outer, float inner)
    {
        var points = new Vector2[10];
        for (int i = 0; i < points.Length; i++)
            points[i] = center + Vector2.FromAngle(i * Mathf.Tau / points.Length - Mathf.Pi / 2) * (i % 2 == 0 ? outer : inner);
        return points;
    }

    void DrawTopBar()
    {
        Stone(_topBar, 0.72f);
        DrawRect(new Rect2(0, TopBarHeight - 5, Size.X, 3), Bronze);
        DrawRect(new Rect2(0, TopBarHeight - 2, Size.X, 2), Edge);
        DrawRect(new Rect2(0, 0, Size.X, 1.5f), new Color(1, 1, 1, 0.12f));

        // Medallion: a stone disc with a bronze ring around a night-blue gem and a gold star.
        var disc = Circle(_medallion, MedallionRadius, 40);
        DrawPolygon(disc, new[] { new Color(1.1f, 1.1f, 1.1f) }, Uvs(disc), _stone);
        DrawArc(_medallion, MedallionRadius, 0, Mathf.Tau, 48, Edge, 2.5f, true);
        DrawCircle(_medallion, 27, Edge);
        DrawArc(_medallion, 25, 0, Mathf.Tau, 48, Bronze, 3.5f, true);
        DrawArc(_medallion, 25.5f, Mathf.Pi * 1.05f, Mathf.Pi * 1.7f, 16, new Color(1f, 0.88f, 0.55f), 1.5f, true);
        DrawCircle(_medallion, 22.5f, new Color(0.04f, 0.06f, 0.18f));
        DrawCircle(_medallion + new Vector2(0, -3), 18, new Color(0.08f, 0.13f, 0.34f));
        DrawCircle(_medallion + new Vector2(0, -6), 11, new Color(0.13f, 0.2f, 0.46f));
        var star = new Vector2[16];
        for (int i = 0; i < star.Length; i++)
            star[i] = _medallion + Vector2.FromAngle(i * Mathf.Tau / star.Length - Mathf.Pi / 2) * (i % 2 == 0 ? (i % 4 == 0 ? 17 : 11) : 4.5f);
        DrawColoredPolygon(star, new Color(0.98f, 0.8f, 0.36f));
        DrawPolyline([.. star, star[0]], new Color(0.45f, 0.3f, 0.08f), 1, true);
        DrawCircle(_medallion, 3, new Color(1f, 0.96f, 0.8f));
    }

    void Stone(Rect2 r, float shade)
    {
        Vector2[] quad = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        DrawPolygon(quad, new[] { new Color(shade, shade, shade) }, Uvs(quad), _stone);
    }

    /// <summary>Lit top and left edges, dark right edge and a black outline.</summary>
    void Bevel(Rect2 r)
    {
        DrawRect(new Rect2(r.Position, r.Size.X, 2), new Color(1, 1, 1, 0.22f));
        DrawRect(new Rect2(r.Position, 2, r.Size.Y), new Color(1, 1, 1, 0.1f));
        DrawRect(new Rect2(r.End.X - 3, r.Position.Y, 3, r.Size.Y), new Color(0, 0, 0, 0.35f));
        DrawRect(r, Edge, filled: false, width: 1.5f);
    }

    void Shadow(Rect2 r, bool down)
    {
        var dark = new Color(0, 0, 0, 0.45f);
        var clear = new Color(0, 0, 0, 0);
        Vector2[] quad = [r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y)];
        DrawPolygon(quad, down ? new[] { dark, dark, clear, clear } : new[] { clear, clear, dark, dark });
    }

    static Vector2[] Uvs(Vector2[] points)
    {
        var uvs = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
            uvs[i] = points[i] / StoneTileSize;
        return uvs;
    }

    /// <summary>A rectangle with a flattened round arch on top, as a polygon.</summary>
    static Vector2[] Arch(Rect2 r, float archHeight, int segments = 20)
    {
        var points = new List<Vector2> { new(r.Position.X, r.End.Y), r.End };
        var center = new Vector2(r.GetCenter().X, r.Position.Y + archHeight);
        for (int i = 0; i <= segments; i++)
        {
            float angle = -Mathf.Pi * i / segments; // right, over the top, to the left
            points.Add(center + new Vector2(Mathf.Cos(angle) * r.Size.X / 2, Mathf.Sin(angle) * archHeight));
        }
        return [.. points];
    }

    static Vector2[] Circle(Vector2 center, float radius, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
            points[i] = center + Vector2.FromAngle(i * Mathf.Tau / segments) * radius;
        return points;
    }
}
