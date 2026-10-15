using System;
using System.Collections.Generic;
using System.Linq;
using CastleFight.Net.Protocol;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

public enum DisplayMode
{
    Windowed,
    Fullscreen,
    ExclusiveFullscreen,
}

/// <summary>A server the player saved or joined before.</summary>
public sealed record ServerEntry(string Name, string Address, int Port)
{
    public string Endpoint => $"{Address}:{Port}";
    public bool SameServer(string address, int port) => Port == port && string.Equals(Address, address, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Everything the player chose last time: name, servers, host setup and display options (user://settings.cfg).</summary>
public sealed class Settings
{
    const string Path = "user://settings.cfg";
    public const int MaxRecent = 6;

    public string PlayerName { get; set; } = System.Environment.UserName;

    // Joining
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = ProtocolInfo.DefaultPort;
    public List<ServerEntry> Favorites { get; } = new();
    public List<ServerEntry> Recent { get; } = new();

    // Hosting
    public int HostPort { get; set; } = ProtocolInfo.DefaultPort;
    public MapId HostMap { get; set; } = MapId.SingleLane;
    public int HostBlueBots { get; set; }
    public int HostRedBots { get; set; } = 1;

    // Display
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Windowed;
    public bool VSync { get; set; } = true;
    public int MaxFps { get; set; }

    public static Settings Load()
    {
        var settings = new Settings();
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return settings;
        settings.PlayerName = file.GetValue("player", "name", settings.PlayerName).AsString();
        settings.Address = file.GetValue("net", "address", settings.Address).AsString();
        settings.Port = file.GetValue("net", "port", settings.Port).AsInt32();
        settings.Favorites.AddRange(LoadServers(file, "favorites"));
        settings.Recent.AddRange(LoadServers(file, "recent").Take(MaxRecent));
        settings.HostPort = file.GetValue("host", "port", settings.HostPort).AsInt32();
        var map = (MapId)file.GetValue("host", "map", (int)settings.HostMap).AsInt32();
        settings.HostMap = MapCatalog.IsValid(map) ? map : MapId.SingleLane;
        settings.HostBlueBots = file.GetValue("host", "blue_bots", settings.HostBlueBots).AsInt32();
        settings.HostRedBots = file.GetValue("host", "red_bots", settings.HostRedBots).AsInt32();
        settings.DisplayMode = (DisplayMode)Math.Clamp(file.GetValue("display", "mode", (int)settings.DisplayMode).AsInt32(), 0, 2);
        settings.VSync = file.GetValue("display", "vsync", settings.VSync).AsBool();
        settings.MaxFps = file.GetValue("display", "max_fps", settings.MaxFps).AsInt32();
        return settings;
    }

    public void Save()
    {
        var file = new ConfigFile();
        file.SetValue("player", "name", PlayerName);
        file.SetValue("net", "address", Address);
        file.SetValue("net", "port", Port);
        file.SetValue("net", "favorites", SaveServers(Favorites));
        file.SetValue("net", "recent", SaveServers(Recent));
        file.SetValue("host", "port", HostPort);
        file.SetValue("host", "map", (int)HostMap);
        file.SetValue("host", "blue_bots", HostBlueBots);
        file.SetValue("host", "red_bots", HostRedBots);
        file.SetValue("display", "mode", (int)DisplayMode);
        file.SetValue("display", "vsync", VSync);
        file.SetValue("display", "max_fps", MaxFps);
        file.Save(Path);
    }

    /// <summary>Moves the server to the top of the recent list.</summary>
    public void AddRecent(string address, int port)
    {
        Recent.RemoveAll(s => s.SameServer(address, port));
        Recent.Insert(0, new ServerEntry(address, address, port));
        if (Recent.Count > MaxRecent)
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    public bool IsFavorite(string address, int port) => Favorites.Any(s => s.SameServer(address, port));

    /// <summary>Window mode, vsync and frame cap. Does nothing visible when running headless.</summary>
    public void ApplyDisplay()
    {
        DisplayServer.WindowSetMode(DisplayMode switch
        {
            DisplayMode.Fullscreen => DisplayServer.WindowMode.Fullscreen,
            DisplayMode.ExclusiveFullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        });
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = MaxFps;
    }

    public static string CleanName(string name)
    {
        name = name.Trim();
        return name.Length == 0 ? "Player" : name[..Math.Min(name.Length, ProtocolInfo.MaxNameLength)];
    }

    static IEnumerable<ServerEntry> LoadServers(ConfigFile file, string key)
    {
        foreach (var item in file.GetValue("net", key, new Godot.Collections.Array()).AsGodotArray())
        {
            var entry = item.AsGodotDictionary();
            string address = entry.TryGetValue("address", out var a) ? a.AsString() : "";
            if (address.Length == 0)
                continue;
            string name = entry.TryGetValue("name", out var n) ? n.AsString() : address;
            int port = entry.TryGetValue("port", out var p) ? p.AsInt32() : ProtocolInfo.DefaultPort;
            yield return new ServerEntry(name, address, port);
        }
    }

    static Godot.Collections.Array SaveServers(IEnumerable<ServerEntry> servers)
    {
        var array = new Godot.Collections.Array();
        foreach (var server in servers)
            array.Add(new Godot.Collections.Dictionary { ["name"] = server.Name, ["address"] = server.Address, ["port"] = server.Port });
        return array;
    }
}
