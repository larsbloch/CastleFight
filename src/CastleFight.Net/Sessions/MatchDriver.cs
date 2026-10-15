using System.Diagnostics;
using CastleFight.Sim;

namespace CastleFight.Net.Sessions;

/// <summary>
/// Fixed-rate game loop shared by the Godot client and the tests: turns frame time into simulation ticks,
/// catches up when bundles piled up, and reports when it is stuck waiting for other players.
/// </summary>
public sealed class MatchDriver(IMatchSession session)
{
    public const double TickSeconds = 1.0 / Simulation.TicksPerSecond;
    const int MaxStepsPerFrame = 5;
    const double WaitingThresholdSeconds = 0.3;

    readonly Stopwatch _stepTimer = new();
    double _accumulator;
    double _starvedSeconds;

    public IMatchSession Session { get; } = session;

    /// <summary>Single player only: stop the clock.</summary>
    public bool Paused { get; set; }

    /// <summary>How far we are between the previous and the current tick (0..1), for interpolation.</summary>
    public float Alpha => (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);

    /// <summary>True when the next tick's commands have been missing for a while (another player is lagging).</summary>
    public bool IsWaiting => _starvedSeconds > WaitingThresholdSeconds;

    public double StepMsAverage { get; private set; }
    public double StepMsWorst { get; private set; }

    public void ResetWorst() => StepMsWorst = 0;

    public void Advance(double deltaSeconds)
    {
        Session.Poll();
        if (Paused && !Session.IsNetworked)
            return;

        _accumulator += deltaSeconds;
        int steps = 0;
        while (steps < MaxStepsPerFrame && Session.CanStep)
        {
            // Online, more buffered bundles than the input delay means we are behind everyone else: hurry.
            bool behind = Session.IsNetworked && Session.BufferedTicks > Session.InputDelayTicks + 1;
            if (_accumulator < TickSeconds && !behind)
                break;

            _stepTimer.Restart();
            Session.Step();
            double ms = _stepTimer.Elapsed.TotalMilliseconds;
            StepMsAverage = StepMsAverage == 0 ? ms : StepMsAverage * 0.95 + ms * 0.05;
            StepMsWorst = Math.Max(StepMsWorst, ms);

            _accumulator = Math.Max(0, _accumulator - TickSeconds);
            steps++;
        }

        bool starved = !Session.CanStep && _accumulator >= TickSeconds;
        _starvedSeconds = starved ? _starvedSeconds + deltaSeconds : 0;

        // Can't keep up (or waiting for the network): drop the backlog rather than spiral.
        _accumulator = Math.Min(_accumulator, TickSeconds);
    }
}
