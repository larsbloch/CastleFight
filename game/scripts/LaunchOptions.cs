using System;
using CastleFight.Net.Protocol;
using CastleFight.Sim;

namespace CastleFight.Game;

/// <summary>
/// Command-line options after <c>--</c>, mainly for automated and multi-window testing:
/// <code>
/// godot --path game -- --map 2 --players-per-team 4      single player on the three-lane map
/// godot --path game -- --host --autostart 2 --bots 2      host, start once 2 players are in and ready
/// godot --path game -- --join 127.0.0.1 --ready --name B  join and ready up
/// --smoke (stress wave + HUD log), --autoplay (a bot plays for you), --quit-at-tick N, --print-hashes, --port P
/// --screen host|join|options (open that menu), --screenshot out.png [--screenshot-frames N] (save the window
/// after N frames, default 150, and quit),
/// --say "text" (send one chat line in the lobby)
/// </code>
/// </summary>
public sealed class LaunchOptions
{
    public bool Smoke { get; private set; }
    public MapId? Map { get; private set; }
    public int PlayersPerTeam { get; private set; } = 3;
    public bool Host { get; private set; }
    public string? JoinAddress { get; private set; }
    public int Port { get; private set; } = ProtocolInfo.DefaultPort;
    public string? Name { get; private set; }
    public int Bots { get; private set; }
    public int AutoStartPlayers { get; private set; }
    public bool Ready { get; private set; }
    public bool AutoPlay { get; private set; }
    public int QuitAtTick { get; private set; }
    public bool PrintHashes { get; private set; }
    public string? Screen { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public int ScreenshotFrames { get; private set; } = 150;
    public string? Say { get; private set; }

    /// <summary>Skip the menu: start or connect straight away.</summary>
    public bool SkipMenu => Smoke || Map is not null || Host || JoinAddress is not null;

    public static LaunchOptions Parse(string[] args)
    {
        var options = new LaunchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--smoke": options.Smoke = true; break;
                case "--host": options.Host = true; break;
                case "--ready": options.Ready = true; break;
                case "--autoplay": options.AutoPlay = true; break;
                case "--print-hashes": options.PrintHashes = true; break;
                case "--join": options.JoinAddress = Next(); break;
                case "--name": options.Name = Next(); break;
                case "--screen": options.Screen = Next(); break;
                case "--screenshot": options.ScreenshotPath = Next(); break;
                case "--screenshot-frames" when int.TryParse(Next(), out int frames): options.ScreenshotFrames = frames; break;
                case "--say": options.Say = Next(); break;
                case "--map" when byte.TryParse(Next(), out byte map) && MapCatalog.IsValid((MapId)map): options.Map = (MapId)map; break;
                case "--port" when int.TryParse(Next(), out int port): options.Port = port; break;
                case "--bots" when int.TryParse(Next(), out int bots): options.Bots = bots; break;
                case "--autostart" when int.TryParse(Next(), out int players): options.AutoStartPlayers = players; break;
                case "--quit-at-tick" when int.TryParse(Next(), out int tick): options.QuitAtTick = tick; break;
                case "--players-per-team" when int.TryParse(Next(), out int count):
                    options.PlayersPerTeam = Math.Clamp(count, 1, ProtocolInfo.TeamCapacity);
                    break;
            }
        }
        return options;
    }
}
