using CastleFight.Sim;

namespace CastleFight.Net.Sessions;

/// <summary>
/// One running match as the game sees it: the simulation plus where its commands come from. The same
/// game loop (<see cref="MatchDriver"/>) drives single player and lockstep multiplayer.
/// </summary>
public interface IMatchSession : IDisposable
{
    Simulation Sim { get; }
    int LocalPlayerId { get; }
    bool IsNetworked { get; }
    int InputDelayTicks { get; }

    /// <summary>A command from the local player; it executes <see cref="InputDelayTicks"/> ticks from now.</summary>
    void Issue(Command command);

    /// <summary>Pumps the network (no-op offline).</summary>
    void Poll();

    /// <summary>Whether the commands for the next tick are known, i.e. <see cref="Step"/> may run.</summary>
    bool CanStep { get; }

    /// <summary>How many ticks could be stepped right now. Above the input delay means we fell behind.</summary>
    int BufferedTicks { get; }

    void Step();
}
