using System;
using System.Collections.Generic;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>One card per map (icon, name, lane count). When not editable it highlights the current map and dims the rest.</summary>
public partial class MapSelector : HBoxContainer
{
    readonly Dictionary<MapId, Button> _cards = new();
    readonly ButtonGroup _group = new();
    bool _editable = true;

    public event Action<MapId>? MapChosen;

    public MapId Selected { get; private set; }

    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            Refresh();
        }
    }

    public void Build(MapId selected, Vector2 iconSize)
    {
        AddThemeConstantOverride("separation", 14);
        Alignment = AlignmentMode.Center;
        foreach (var map in MapCatalog.All)
        {
            var card = new Button
            {
                ThemeTypeVariation = "CardButton",
                ToggleMode = true,
                ButtonGroup = _group,
                CustomMinimumSize = iconSize + new Vector2(20, 66),
            };
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.SetAnchorsPreset(LayoutPreset.FullRect);
            column.OffsetLeft = column.OffsetTop = 10;
            column.OffsetRight = column.OffsetBottom = -8;
            column.AddThemeConstantOverride("separation", 2);
            column.AddChild(new MapIcon { Map = map, CustomMinimumSize = iconSize, SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
            var name = new Label { Text = MapCatalog.DisplayName(map), ThemeTypeVariation = "SubHeaderLabel", HorizontalAlignment = HorizontalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 16);
            column.AddChild(name);
            int lanes = MapCatalog.Get(map).Lanes.Count;
            var caption = new Label { Text = lanes == 1 ? "1 lane" : $"{lanes} lanes", ThemeTypeVariation = "MutedLabel", HorizontalAlignment = HorizontalAlignment.Center };
            caption.AddThemeFontSizeOverride("font_size", 15);
            column.AddChild(caption);
            foreach (var child in column.GetChildren())
                ((Control)child).MouseFilter = MouseFilterEnum.Ignore;
            card.AddChild(column);

            card.Pressed += () =>
            {
                Selected = map;
                Refresh();
                MapChosen?.Invoke(map);
            };
            _cards[map] = card;
            AddChild(card);
        }
        Select(selected);
    }

    /// <summary>Shows <paramref name="map"/> as chosen without raising <see cref="MapChosen"/>.</summary>
    public void Select(MapId map)
    {
        Selected = map;
        foreach (var (id, card) in _cards)
            card.SetPressedNoSignal(id == map);
        Refresh();
    }

    void Refresh()
    {
        foreach (var (id, card) in _cards)
        {
            card.MouseFilter = _editable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            card.FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None;
            card.Modulate = _editable || id == Selected ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
        }
    }
}
