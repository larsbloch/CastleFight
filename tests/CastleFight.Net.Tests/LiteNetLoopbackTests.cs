using System.Diagnostics;
using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Net.Relay;
using CastleFight.Net.Sessions;
using CastleFight.Net.Transport;

namespace CastleFight.Net.Tests;

/// <summary>Real UDP over 127.0.0.1. Skip with <c>--filter Category!=Network</c> if a firewall gets in the way.</summary>
[Trait("Category", "Network")]
public class LiteNetLoopbackTests
{
    [Fact]
    public void A_hosted_relay_and_two_udp_clients_play_in_sync()
    {
        using var host = new RelayHost(port: 0, new RelayOptions { SeedSource = () => 99 });
        var first = new LockstepClient(new LiteNetClientTransport("127.0.0.1", host.Port), "First");
        Assert.True(PumpUntil([first], () => first.State == ClientState.Lobby)); // first in: hosts
        var second = new LockstepClient(new LiteNetClientTransport("127.0.0.1", host.Port), "Second");
        var clients = new[] { first, second };
        try
        {
            Assert.True(first.IsHost);
            Assert.True(PumpUntil(clients, () => clients.All(c => c.State == ClientState.Lobby) &&
                                               first.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 2));
            second.SetReady(true);
            first.AddBot(0);
            Assert.True(PumpUntil(clients, () => first.Lobby!.Slots.Count(s => s.Kind != SlotKind.Empty) == 3 &&
                                               first.Lobby.Slots.Any(s => s.Kind == SlotKind.Human && s.Ready)));
            first.StartGame();
            Assert.True(PumpUntil(clients, () => clients.All(c => c.State == ClientState.InGame)));

            var sessions = clients.Select(c => new NetworkSession(c, autoPlayLocal: true)).ToArray();
            var drivers = sessions.Select(s => new MatchDriver(s)).ToArray();
            var hashes = sessions.Select(_ => new Dictionary<int, ulong>()).ToArray();
            for (int i = 0; i < sessions.Length; i++)
            {
                var mine = hashes[i];
                sessions[i].HashComputed += (tick, hash) => mine[tick] = hash;
            }

            var clock = Stopwatch.StartNew();
            double last = 0;
            while (sessions.Any(s => s.Sim.Tick < 100) && clock.Elapsed < TimeSpan.FromSeconds(30))
            {
                double now = clock.Elapsed.TotalSeconds;
                foreach (var driver in drivers)
                    driver.Advance(now - last);
                last = now;
                Thread.Sleep(2);
            }

            Assert.All(sessions, s => Assert.True(s.Sim.Tick >= 100, $"only reached tick {s.Sim.Tick}"));
            Assert.Null(host.Error);
            Assert.Equal(hashes[0][100], hashes[1][100]);
            Assert.True(first.RoundTripMs >= 0);
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }
    }

    static bool PumpUntil(LockstepClient[] clients, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            foreach (var client in clients)
                client.Poll();
            if (condition())
                return true;
            Thread.Sleep(2);
        }
        return false;
    }
}
