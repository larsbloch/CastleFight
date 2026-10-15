using CastleFight.Sim;
using CastleFight.Sim.Bots;

namespace CastleFight.Net.Sessions;

/// <summary>Single player: every other player is a bot, and commands are scheduled exactly as they would be online.</summary>
public sealed class LocalSession : IMatchSession
{
    readonly CommandSchedule _schedule = new();
    readonly ScriptedBot[] _bots;

    public LocalSession(GameSetup setup, int localPlayerId, int inputDelayTicks = 3)
    {
        Sim = new Simulation(setup);
        LocalPlayerId = localPlayerId;
        InputDelayTicks = inputDelayTicks;
        _bots = Enumerable.Range(0, setup.PlayerTeams.Count)
            .Where(id => id != localPlayerId)
            .Select(id => new ScriptedBot(id, setup.Seed))
            .ToArray();
    }

    public Simulation Sim { get; }
    public int LocalPlayerId { get; }
    public bool IsNetworked => false;
    public int InputDelayTicks { get; }
    public bool CanStep => true;
    public int BufferedTicks => 1;

    public void Issue(Command command) => _schedule.Add(Sim.Tick + InputDelayTicks, command);

    public void Poll() { }

    public void Step()
    {
        foreach (var bot in _bots)
        {
            if (bot.TryThink(Sim, out var command))
                _schedule.Add(Sim.Tick + InputDelayTicks, command);
        }
        Sim.Step(_schedule.TakeForTick(Sim.Tick));
    }

    public void Dispose() { }
}
