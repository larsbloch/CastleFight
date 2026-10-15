using CastleFight.Net.Protocol;
using CastleFight.Net.Relay;

// Standalone lockstep relay, for hosting on a server instead of in a player's game:
//
//   dotnet run -c Release --project tools/CastleFight.Server -- --port 7777
//
// Players choose Join and enter this machine's address. The first to join is the lobby host.
// --allow-build-mismatch lets different game builds play together (development only: they may desync).

int port = ProtocolInfo.DefaultPort;
bool allowBuildMismatch = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
        case "--allow-build-mismatch": allowBuildMismatch = true; break;
        default: throw new ArgumentException($"Unknown option {args[i]}");
    }
}

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

// A relay hosts one game. When it ends (or everyone leaves mid-game), start a fresh one on the same port.
while (!stop.IsSet)
{
    using var host = new RelayHost(port, new RelayOptions { AllowBuildMismatch = allowBuildMismatch }, Log);
    Log($"CastleFight relay (protocol {ProtocolInfo.Version}) listening on UDP port {host.Port}. Ctrl+C to stop.");
    while (!stop.IsSet && host.Server.State is RelayState.Lobby or RelayState.InGame or RelayState.Halted && host.Error is null)
        stop.Wait(500);
    if (host.Error is not null)
        Log("Relay crashed; restarting.");
    else if (!stop.IsSet)
        Log("Game over; opening a new lobby.");
}

static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
