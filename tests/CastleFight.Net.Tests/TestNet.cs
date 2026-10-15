using CastleFight.Net.Client;
using CastleFight.Net.Relay;
using CastleFight.Net.Sessions;
using CastleFight.Net.Transport;

namespace CastleFight.Net.Tests;

/// <summary>A relay and clients on an in-memory network, driven in 5 ms steps of virtual time.</summary>
sealed class TestNet : IDisposable
{
    public const int Port = 7777;
    const int StepMs = 5;

    readonly List<(LockstepClient Client, MatchDriver? Driver, double Speed)> _players = new();

    public TestNet(NetworkConditions conditions, RelayOptions? options = null)
    {
        Network = new InMemoryNetwork(conditions);
        Relay = new RelayServer(Network.Listen(Port), options ?? new RelayOptions { SeedSource = () => 1234 });
    }

    public InMemoryNetwork Network { get; }
    public RelayServer Relay { get; }
    public IReadOnlyList<LockstepClient> Clients => _players.Select(p => p.Client).ToList();

    public LockstepClient Join(string name, ushort? protocolVersion = null, Guid? buildId = null)
    {
        var client = new LockstepClient(Network.Connect(Port), name, protocolVersion, buildId);
        _players.Add((client, null, 1.0));
        return client;
    }

    /// <summary>Starts stepping a client's session. <paramref name="speed"/> skews its frame clock (slow or fast PCs).</summary>
    public MatchDriver Drive(LockstepClient client, IMatchSession session, double speed = 1.0)
    {
        var driver = new MatchDriver(session);
        int index = _players.FindIndex(p => p.Client == client);
        _players[index] = (client, driver, speed);
        return driver;
    }

    public void StopDriving(LockstepClient client)
    {
        int index = _players.FindIndex(p => p.Client == client);
        _players.RemoveAt(index);
    }

    public void Run(int milliseconds)
    {
        for (int elapsed = 0; elapsed < milliseconds; elapsed += StepMs)
            Step();
    }

    public bool RunUntil(Func<bool> condition, int maxMilliseconds = 10_000)
    {
        for (int elapsed = 0; elapsed < maxMilliseconds; elapsed += StepMs)
        {
            if (condition())
                return true;
            Step();
        }
        return condition();
    }

    void Step()
    {
        Network.Advance(StepMs);
        Relay.Poll();
        foreach (var (client, driver, speed) in _players.ToArray())
        {
            if (driver is null)
                client.Poll();
            else
                driver.Advance(StepMs / 1000.0 * speed);
        }
    }

    public void Dispose()
    {
        foreach (var (client, _, _) in _players)
            client.Dispose();
        Relay.Dispose();
    }
}
