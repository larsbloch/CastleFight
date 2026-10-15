using CastleFight.Net.Client;
using CastleFight.Sim;
using CastleFight.Sim.Bots;

namespace CastleFight.Net.Sessions;

/// <summary>
/// Lockstep multiplayer: a tick is only stepped once the relay's bundle for it has arrived. Local commands
/// (and, on the host, the bots' commands) are sent as input for tick + input delay, and a state hash is
/// reported every second so the relay can spot desyncs.
/// </summary>
public sealed class NetworkSession : IMatchSession
{
    readonly LockstepClient _client;
    readonly List<Command> _pending = new();
    readonly List<ScriptedBot> _bots = new();
    readonly int _hashInterval;
    bool _dumpRaised;

    /// <param name="autoPlayLocal">Let a bot play for the local player too (headless tests and soak runs).</param>
    public NetworkSession(LockstepClient client, bool autoPlayLocal = false, int hashIntervalTicks = Simulation.TicksPerSecond)
    {
        var start = client.Start ?? throw new InvalidOperationException("The game has not started yet.");
        _client = client;
        _hashInterval = hashIntervalTicks;
        Sim = new Simulation(start.ToSetup());
        LocalPlayerId = client.LocalPlayerId;
        InputDelayTicks = start.InputDelayTicks;

        // The host plays the bots: it is the only client allowed to send their commands.
        for (int id = 0; id < start.Players.Length; id++)
        {
            if ((client.IsHost && start.Players[id].IsBot) || (autoPlayLocal && id == LocalPlayerId))
                _bots.Add(new ScriptedBot(id, start.Seed));
        }
    }

    public Simulation Sim { get; }
    public int LocalPlayerId { get; }
    public bool IsNetworked => true;
    public int InputDelayTicks { get; }
    public LockstepClient Client => _client;

    /// <summary>Raised once, at the relay's dump tick, after a desync. Write <see cref="Simulation.DumpState"/> then.</summary>
    public event Action<Simulation>? DumpRequested;

    /// <summary>Raised with every state hash sent to the relay (tick, hash).</summary>
    public event Action<int, ulong>? HashComputed;

    public ulong LastHash { get; private set; }

    public bool CanStep => !AtDumpTick && _client.HasBundle(Sim.Tick);

    public int BufferedTicks => AtDumpTick ? 0 : _client.BufferedFrom(Sim.Tick);

    bool AtDumpTick => _client.Desync is { } desync && Sim.Tick >= desync.DumpTick;

    public void Issue(Command command) => _pending.Add(command);

    public void Poll()
    {
        _client.Poll();
        if (AtDumpTick && !_dumpRaised)
        {
            _dumpRaised = true;
            DumpRequested?.Invoke(Sim);
        }
    }

    public void Step()
    {
        if (!_client.TryTakeBundle(Sim.Tick, out var bundle))
            throw new InvalidOperationException($"No bundle for tick {Sim.Tick}; check CanStep first.");

        foreach (var bot in _bots)
        {
            if (bot.TryThink(Sim, out var command))
                _pending.Add(command);
        }
        _client.SendInput(Sim.Tick + InputDelayTicks, _pending);
        _pending.Clear();

        Sim.Step(bundle);

        if (Sim.Tick % _hashInterval == 0)
        {
            LastHash = Sim.ComputeStateHash();
            _client.SendHash(Sim.Tick, LastHash);
            HashComputed?.Invoke(Sim.Tick, LastHash);
        }
        if (AtDumpTick && !_dumpRaised)
        {
            _dumpRaised = true;
            DumpRequested?.Invoke(Sim);
        }
    }

    public void Dispose() => _client.Dispose();
}
