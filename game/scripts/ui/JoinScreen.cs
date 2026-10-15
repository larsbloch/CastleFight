using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// Favourite and recent servers on the left, the server being joined on the right. Clicking a server fills in
/// the form; double-clicking joins it. Favourites are saved to <see cref="Settings"/> straight away.
/// </summary>
public partial class JoinScreen : Control
{
    Settings _settings = null!;
    VBoxContainer _favorites = null!;
    VBoxContainer _recent = null!;
    LineEdit _name = null!;
    LineEdit _address = null!;
    SpinBox _port = null!;
    Button _favoriteButton = null!;
    Label _status = null!;
    readonly ButtonGroup _rows = new();

    public event Action<string, int>? JoinRequested;
    public event Action? BackRequested;

    public void Build(Settings settings, string? status)
    {
        _settings = settings;
        var column = UiBuilder.Page(this, "Join Game", 1080, titleSize: 64);

        var columns = UiBuilder.Row();
        columns.AddThemeConstantOverride("separation", 24);
        column.AddChild(columns);

        // Left: server lists.
        var lists = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.35f };
        lists.AddThemeConstantOverride("separation", 8);
        lists.AddChild(UiBuilder.SubHeading("Favorite Servers"));
        _favorites = List(lists, 230);
        lists.AddChild(UiBuilder.SubHeading("Recently Joined"));
        _recent = List(lists, 150);
        columns.AddChild(lists);

        // Right: the server form.
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 10);
        form.AddChild(UiBuilder.SubHeading("Server"));
        _name = new LineEdit { PlaceholderText = "Name for your favorites", MaxLength = 32, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _address = new LineEdit { Text = settings.Address, PlaceholderText = "IP address or host name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _port = UiBuilder.PortBox(settings.Port);
        form.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Name", 100), _name));
        form.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Address", 100), _address));
        form.AddChild(UiBuilder.Row(UiBuilder.FieldLabel("Port", 100), _port));
        _favoriteButton = UiBuilder.Button("Save as Favorite", ToggleFavorite, expand: true);
        form.AddChild(_favoriteButton);
        form.AddChild(UiBuilder.Note("Ask the host for their public IP address (or LAN address on the same network) and port."));
        form.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var join = UiBuilder.Button("Join", Join, variation: "BigButton");
        join.CustomMinimumSize = new Vector2(0, 62);
        form.AddChild(join);
        columns.AddChild(form);

        _status = UiBuilder.Text(status ?? "", UiTheme.Warning);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.VerticalAlignment = VerticalAlignment.Center;
        column.AddChild(UiBuilder.Row(UiBuilder.Button("Back", () => BackRequested?.Invoke(), minWidth: 150), _status));

        _address.TextChanged += _ => UpdateFavoriteButton();
        _address.TextSubmitted += _ => Join();
        _port.ValueChanged += _ => UpdateFavoriteButton();
        Refresh();
    }

    static VBoxContainer List(VBoxContainer parent, int height)
    {
        var well = new PanelContainer { ThemeTypeVariation = "InsetPanel", CustomMinimumSize = new Vector2(0, height) };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        well.AddChild(scroll);
        parent.AddChild(well);
        return list;
    }

    void Refresh()
    {
        Fill(_favorites, _settings.Favorites, "No favorites yet. Fill in a server on the right and save it.");
        Fill(_recent, _settings.Recent, "Servers you join show up here.");
        UpdateFavoriteButton();
    }

    void Fill(VBoxContainer list, List<ServerEntry> servers, string emptyText)
    {
        UiBuilder.Clear(list);
        if (servers.Count == 0)
            list.AddChild(UiBuilder.Note(emptyText));
        foreach (var server in servers)
            list.AddChild(ServerRow(server));
    }

    Control ServerRow(ServerEntry server)
    {
        var row = new Button { ThemeTypeVariation = "ListRow", ToggleMode = true, ButtonGroup = _rows, CustomMinimumSize = new Vector2(0, 46) };
        var content = UiBuilder.Row();
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = 10;
        content.OffsetRight = -6;
        content.MouseFilter = MouseFilterEnum.Ignore;
        var name = new Label { Text = server.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        name.AddThemeFontOverride("font", UiTheme.BodyBoldFont);
        var endpoint = new Label { Text = server.Endpoint, ThemeTypeVariation = "MutedLabel", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        content.AddChild(name);
        if (server.Name != server.Address)
            content.AddChild(endpoint);
        content.AddChild(UiBuilder.SmallButton("Join", () =>
        {
            Fill(server);
            Join();
        }));
        row.AddChild(content);

        row.Pressed += () => Fill(server);
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left })
                Join();
        };
        return row;
    }

    void Fill(ServerEntry server)
    {
        _name.Text = server.Name == server.Address ? "" : server.Name;
        _address.Text = server.Address;
        _port.Value = server.Port;
        UpdateFavoriteButton();
    }

    void ToggleFavorite()
    {
        string address = _address.Text.Trim();
        int port = (int)_port.Value;
        if (address.Length == 0)
        {
            _status.Text = "Enter the server's address first.";
            return;
        }
        int existing = _settings.Favorites.FindIndex(s => s.SameServer(address, port));
        if (existing >= 0)
        {
            _settings.Favorites.RemoveAt(existing);
        }
        else
        {
            string name = _name.Text.Trim();
            _settings.Favorites.Add(new ServerEntry(name.Length > 0 ? name : address, address, port));
        }
        _settings.Save();
        _status.Text = "";
        Refresh();
    }

    void UpdateFavoriteButton() =>
        _favoriteButton.Text = _settings.IsFavorite(_address.Text.Trim(), (int)_port.Value) ? "Remove from Favorites" : "Save as Favorite";

    void Join()
    {
        string address = _address.Text.Trim();
        if (address.Length == 0)
        {
            _status.Text = "Enter the server's address.";
            return;
        }
        _settings.Address = address;
        _settings.Port = (int)_port.Value;
        JoinRequested?.Invoke(address, (int)_port.Value);
    }
}
