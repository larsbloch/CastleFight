using System;
using CastleFight.Net.Protocol;
using Godot;

namespace CastleFight.Game;

/// <summary>Remembers the player's name and the last server they joined (user://settings.cfg).</summary>
public sealed class Settings
{
    const string Path = "user://settings.cfg";

    public string PlayerName { get; set; } = System.Environment.UserName;
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = ProtocolInfo.DefaultPort;

    public static Settings Load()
    {
        var settings = new Settings();
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return settings;
        settings.PlayerName = file.GetValue("player", "name", settings.PlayerName).AsString();
        settings.Address = file.GetValue("net", "address", settings.Address).AsString();
        settings.Port = file.GetValue("net", "port", settings.Port).AsInt32();
        return settings;
    }

    public void Save()
    {
        var file = new ConfigFile();
        file.SetValue("player", "name", PlayerName);
        file.SetValue("net", "address", Address);
        file.SetValue("net", "port", Port);
        file.Save(Path);
    }

    public static string CleanName(string name)
    {
        name = name.Trim();
        return name.Length == 0 ? "Player" : name[..Math.Min(name.Length, ProtocolInfo.MaxNameLength)];
    }
}
