using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Net.Relay;
using CastleFight.Net.Sessions;
using CastleFight.Net.Transport;
using CastleFight.Sim;

namespace CastleFight.Net.Tests;

public class LockstepTests
{
    /// <summary>Joins <paramref name="humans"/> clients, fills up with host bots, starts and drives every client.</summary>
    static (List<LockstepClient> Clients, List<NetworkSession> Sessions) StartGame(TestNet net, int humans, int bots, MapId map,
        bool autoPlay = true, double[]? speeds = null)
    {
        var clients = Enumerable.Range(0, humans).Select(i => net.Join($"P{i}")).ToList();
        Assert.True(net.RunUntil(() => clients.All(c => c.State == ClientState.Lobby && c.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == humans)));
        // With jitter, join order is random; the first to arrive hosts. Put the host first.
        clients = clients.OrderBy(c => c.IsHost ? 0 : 1).ThenBy(c => c.LocalSlot).ToList();
        var host = clients[0];
        host.SetMap(map);
        for (int i = 0; i < bots; i++)
            host.AddBot(team: (humans + i) % 2);
        foreach (var guest in clients.Skip(1))
            guest.SetReady(true);
        Assert.True(net.RunUntil(() => host.Lobby!.Slots.Count(s => s.Kind != SlotKind.Empty) == humans + bots &&
                                       host.Lobby.Slots.Where(s => s.Kind == SlotKind.Human).Count(s => s.Ready) == humans - 1));
        host.StartGame();
        Assert.True(net.RunUntil(() => clients.All(c => c.State == ClientState.InGame)));

        var sessions = new List<NetworkSession>();
        for (int i = 0; i < clients.Count; i++)
        {
            var session = new NetworkSession(clients[i], autoPlayLocal: autoPlay);
            sessions.Add(session);
            net.Drive(clients[i], session, speeds?[i] ?? 1.0);
        }
        return (clients, sessions);
    }

    /// <summary>Records every hash each session reported; returns the ticks where they disagree.</summary>
    static Dictionary<int, ulong>[] RecordHashes(List<NetworkSession> sessions)
    {
        var hashes = sessions.Select(_ => new Dictionary<int, ulong>()).ToArray();
        for (int i = 0; i < sessions.Count; i++)
        {
            var mine = hashes[i];
            sessions[i].HashComputed += (tick, hash) => mine[tick] = hash;
        }
        return hashes;
    }

    static void AssertSameHashes(IReadOnlyList<Dictionary<int, ulong>> hashes, int upToTick)
    {
        for (int tick = Simulation.TicksPerSecond; tick <= upToTick; tick += Simulation.TicksPerSecond)
        {
            var atTick = hashes.Where(h => h.ContainsKey(tick)).Select(h => h[tick]).ToList();
            Assert.True(atTick.Count == hashes.Count, $"only {atTick.Count} of {hashes.Count} clients reported tick {tick}");
            Assert.True(atTick.Distinct().Count() == 1, $"hashes differ at tick {tick}");
        }
    }

    [Fact]
    public void Eight_clients_with_jitter_stay_in_sync_for_2000_ticks()
    {
        using var net = new TestNet(new NetworkConditions(MinLatencyMs: 20, MaxLatencyMs: 150, Seed: 7));
        double[] speeds = [1.0, 0.92, 1.08, 0.97, 1.03, 0.9, 1.1, 1.0]; // some PCs' clocks run fast, some slow
        var (clients, sessions) = StartGame(net, humans: 8, bots: 0, MapId.TwoLanes, speeds: speeds);
        var hashes = RecordHashes(sessions);

        const int ticks = 2000;
        Assert.True(net.RunUntil(() => sessions.All(s => s.Sim.Tick >= ticks), maxMilliseconds: 200_000),
            $"stalled at ticks {string.Join(", ", sessions.Select(s => s.Sim.Tick))}");

        Assert.All(clients, c => Assert.Null(c.Desync));
        Assert.Equal(RelayState.InGame, net.Relay.State);
        AssertSameHashes(hashes, ticks);
        Assert.True(sessions[0].Sim.Units.AliveCount > 20, "the bots should have built an army");
    }

    [Fact]
    public void Packets_overtaking_each_other_do_not_matter()
    {
        using var net = new TestNet(new NetworkConditions(MinLatencyMs: 10, MaxLatencyMs: 40, Seed: 3));
        var (clients, sessions) = StartGame(net, humans: 3, bots: 5, MapId.ThreeLanes);
        var hashes = RecordHashes(sessions);
        net.Network.Conditions = new NetworkConditions(MinLatencyMs: 5, MaxLatencyMs: 200, PreserveOrder: false, Seed: 3);

        Assert.True(net.RunUntil(() => sessions.All(s => s.Sim.Tick >= 1200), maxMilliseconds: 150_000));
        Assert.All(clients, c => Assert.Null(c.Desync));
        AssertSameHashes(hashes, 1200);

        // The host played the five bots; they must have done something.
        var sim = sessions[1].Sim;
        Assert.Contains(Enumerable.Range(0, sim.Units.HighWater), i => sim.Units.Alive[i] && sim.Units.IsStructure[i] && sim.Units.Owner[i] >= 3 && sim.Units.Owner[i] < 8);
    }

    [Fact]
    public void A_player_dropping_out_does_not_stall_or_desync_the_others()
    {
        using var net = new TestNet(new NetworkConditions(MinLatencyMs: 20, MaxLatencyMs: 80, Seed: 11));
        var (clients, sessions) = StartGame(net, humans: 3, bots: 1, MapId.SingleLane);
        var hashes = RecordHashes(sessions);
        var left = new List<int>();
        clients[0].PlayerLeft += left.Add;

        Assert.True(net.RunUntil(() => sessions[2].Sim.Tick >= 500, maxMilliseconds: 60_000));
        int droppedPlayer = clients[2].LocalPlayerId;
        net.StopDriving(clients[2]);
        net.Network.Drop(clients[2].Transport);

        Assert.True(net.RunUntil(() => sessions[0].Sim.Tick >= 1500 && sessions[1].Sim.Tick >= 1500, maxMilliseconds: 120_000),
            $"stalled at {sessions[0].Sim.Tick}/{sessions[1].Sim.Tick}");
        Assert.Equal([droppedPlayer], left);
        Assert.Null(clients[0].Desync);
        AssertSameHashes(hashes.Take(2).ToArray(), 1500);
    }

    [Fact]
    public void A_desync_is_detected_and_everyone_dumps_at_the_same_tick()
    {
        using var net = new TestNet(new NetworkConditions(MinLatencyMs: 20, MaxLatencyMs: 60, Seed: 5));
        var (clients, sessions) = StartGame(net, humans: 2, bots: 0, MapId.SingleLane);
        var dumps = new string?[2];
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            sessions[i].DumpRequested += sim =>
            {
                var writer = new StringWriter();
                sim.DumpState(writer);
                dumps[index] = writer.ToString();
            };
        }

        Assert.True(net.RunUntil(() => sessions[1].Sim.Tick >= 300, maxMilliseconds: 60_000));
        // Corrupt one client's state, as a non-deterministic bug would.
        var sim1 = sessions[1].Sim;
        int victim = Enumerable.Range(0, sim1.Units.HighWater).First(i => sim1.Units.Alive[i]);
        sim1.Units.Hp[victim] -= 1;

        Assert.True(net.RunUntil(() => dumps.All(d => d is not null), maxMilliseconds: 30_000));
        Assert.Equal(RelayState.Halted, net.Relay.State);
        Assert.NotNull(clients[0].Desync);
        Assert.Equal(sessions[0].Sim.Tick, sessions[1].Sim.Tick);
        Assert.Equal(clients[0].Desync!.DumpTick, sessions[0].Sim.Tick);

        var lines0 = dumps[0]!.Split('\n');
        var lines1 = dumps[1]!.Split('\n');
        Assert.Equal(lines0.Length, lines1.Length);
        var different = lines0.Zip(lines1).Where(pair => pair.First != pair.Second).ToList();
        Assert.True(different.Count >= 1);
        Assert.All(different, pair => Assert.Contains("hp=", pair.First));
    }

    [Fact]
    public void Commands_for_someone_elses_player_are_dropped()
    {
        using var net = new TestNet(new NetworkConditions(MinLatencyMs: 10, MaxLatencyMs: 20, Seed: 1));
        var (clients, sessions) = StartGame(net, humans: 2, bots: 0, MapId.SingleLane, autoPlay: false);
        var guest = sessions[1];
        var hostSim = sessions[0].Sim;
        int hostPlayer = clients[0].LocalPlayerId;
        var zone = hostSim.Map.BuildZone(hostSim.Players[hostPlayer].Team);

        // The guest tries to build in the host's name; the host's own command goes through.
        guest.Issue(Command.PlaceBuilding(hostPlayer, UnitCatalog.Barracks, zone.MinX, zone.MinY));
        sessions[0].Issue(Command.PlaceBuilding(hostPlayer, UnitCatalog.Kennel, zone.MinX + 4, zone.MinY));
        net.RunUntil(() => hostSim.Tick >= 40, maxMilliseconds: 20_000);

        int structuresOfHost = Enumerable.Range(0, hostSim.Units.HighWater)
            .Count(i => hostSim.Units.Alive[i] && hostSim.Units.IsStructure[i] && hostSim.Units.Owner[i] == hostPlayer);
        Assert.Equal(1, structuresOfHost);
        Assert.Equal(UnitCatalog.Kennel, hostSim.Units.Type[hostSim.TileOccupant(zone.MinX + 4, zone.MinY)]);
        Assert.Equal(-1, hostSim.TileOccupant(zone.MinX, zone.MinY));
    }
}
