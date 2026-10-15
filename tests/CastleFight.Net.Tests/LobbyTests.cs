using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using CastleFight.Net.Relay;
using CastleFight.Net.Transport;
using CastleFight.Sim;

namespace CastleFight.Net.Tests;

public class LobbyTests
{
    static TestNet LowLatency() => new(new NetworkConditions(5, 15));

    [Fact]
    public void Players_are_welcomed_balanced_over_the_teams_and_the_first_one_hosts()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        var second = net.Join("Second");
        var third = net.Join("Third");
        Assert.True(net.RunUntil(() => third.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 3));

        Assert.All(net.Clients, c => Assert.Equal(ClientState.Lobby, c.State));
        Assert.True(host.IsHost);
        Assert.False(second.IsHost);
        var slots = third.Lobby!.Slots;
        Assert.Equal([0, 1, 0], new[] { host, second, third }.Select(c => (int)slots[c.LocalSlot].Team));
        Assert.Equal("Second", slots[second.LocalSlot].Name);
    }

    [Fact]
    public void A_team_holds_at_most_four()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        net.RunUntil(() => host.State == ClientState.Lobby);
        for (int i = 0; i < 4; i++)
            host.AddBot(team: 0);
        net.Run(200);

        var slots = host.Lobby!.Slots;
        Assert.Equal(4, slots.Count(s => s.Kind != SlotKind.Empty && s.Team == 0)); // host + 3 bots
        Assert.Equal(3, slots.Count(s => s.Kind == SlotKind.Bot));

        host.SetTeam(1);
        host.AddBot(team: 0);
        net.Run(200);
        Assert.Equal(1, host.Lobby!.Slots[host.LocalSlot].Team);
        Assert.Equal(4, host.Lobby.Slots.Count(s => s.Kind == SlotKind.Bot));
    }

    [Fact]
    public void Only_the_host_can_add_bots_change_the_map_or_start()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        var guest = net.Join("Guest");
        net.RunUntil(() => guest.State == ClientState.Lobby && host.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 2);

        guest.AddBot(0);
        guest.SetMap(MapId.ThreeLanes);
        guest.SetReady(true);
        guest.StartGame();
        net.Run(200);
        Assert.Equal(RelayState.Lobby, net.Relay.State);
        Assert.Equal(MapId.SingleLane, guest.Lobby!.Map);
        Assert.DoesNotContain(guest.Lobby.Slots, s => s.Kind == SlotKind.Bot);

        host.SetMap(MapId.ThreeLanes);
        host.AddBot(1);
        host.StartGame();
        Assert.True(net.RunUntil(() => guest.State == ClientState.InGame && host.State == ClientState.InGame));
        Assert.Equal(MapId.ThreeLanes, guest.Start!.Map);
        Assert.Equal(MapId.ThreeLanes, guest.Start.ToSetup().Map);
        Assert.Equal(3, guest.Start.Players.Length);
        Assert.Equal(host.Start, guest.Start);
    }

    [Fact]
    public void The_host_waits_until_everyone_is_ready()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        var guest = net.Join("Guest");
        net.RunUntil(() => host.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 2);

        host.StartGame();
        net.Run(200);
        Assert.Equal(RelayState.Lobby, net.Relay.State);

        guest.SetReady(true);
        net.Run(100);
        host.StartGame();
        Assert.True(net.RunUntil(() => host.State == ClientState.InGame && guest.State == ClientState.InGame));
    }

    [Fact]
    public void Hosting_passes_on_when_the_host_leaves_the_lobby()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        var guest = net.Join("Guest");
        net.RunUntil(() => guest.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 2);

        host.Leave();
        Assert.True(net.RunUntil(() => guest.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 1));
        Assert.True(guest.IsHost);
    }

    [Fact]
    public void Chat_reaches_everyone_with_the_senders_name_and_team()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        var guest = net.Join("Guest");
        net.RunUntil(() => guest.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 2);

        guest.SendChat("  hello\nthere  ");
        guest.SendChat("   ");
        Assert.True(net.RunUntil(() => host.Chat.Any(c => !c.IsSystem)));
        net.Run(200);

        foreach (var client in new[] { host, guest })
        {
            var line = Assert.Single(client.Chat, c => !c.IsSystem);
            Assert.Equal("hello there", line.Text); // one trimmed line; the blank message was dropped
            Assert.Equal("Guest", line.Name);
            Assert.Equal(guest.LocalSlot, line.Slot);
            Assert.Equal(1, line.Team);
        }

        guest.SendChat(new string('a', 200));
        Assert.True(net.RunUntil(() => host.Chat.Count(c => !c.IsSystem) == 2));
        Assert.Equal(ProtocolInfo.MaxChatLength, host.Chat[^1].Text.Length);
    }

    [Fact]
    public void The_relay_announces_joins_leaves_and_host_changes_in_the_chat()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        net.RunUntil(() => host.State == ClientState.Lobby);
        var guest = net.Join("Guest");
        var third = net.Join("Third");
        net.RunUntil(() => third.Lobby?.Slots.Count(s => s.Kind == SlotKind.Human) == 3);

        host.Leave();
        Assert.True(net.RunUntil(() => guest.Chat.Any(c => c.Text.Contains("now the host"))));

        Assert.All(guest.Chat, c => Assert.True(c.IsSystem));
        Assert.Equal(["Guest joined.", "Third joined.", "Host left.", "Guest is now the host."], guest.Chat.Select(c => c.Text));
        Assert.Equal("Host opened the lobby.", host.Chat[0].Text);
    }

    [Fact]
    public void Chat_is_not_relayed_once_the_game_runs()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        net.RunUntil(() => host.State == ClientState.Lobby);
        host.StartGame();
        Assert.True(net.RunUntil(() => host.State == ClientState.InGame));
        int before = host.Chat.Count;

        host.SendChat("anyone?");
        net.Run(300);
        Assert.Equal(before, host.Chat.Count);
    }

    [Fact]
    public void A_different_protocol_version_is_rejected()
    {
        using var net = LowLatency();
        var old = net.Join("Old", protocolVersion: ProtocolInfo.Version + 1);
        Assert.True(net.RunUntil(() => old.State == ClientState.Rejected));
        Assert.Equal(RejectReason.VersionMismatch, old.RejectReason);
    }

    [Fact]
    public void A_different_build_than_the_hosts_is_rejected()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        net.RunUntil(() => host.State == ClientState.Lobby);
        var other = net.Join("Other", buildId: Guid.NewGuid());
        Assert.True(net.RunUntil(() => other.State == ClientState.Rejected));
        Assert.Equal(RejectReason.BuildMismatch, other.RejectReason);

        using var lenient = new TestNet(new NetworkConditions(5, 15), new RelayOptions { AllowBuildMismatch = true });
        lenient.Join("Host");
        var allowed = lenient.Join("Other", buildId: Guid.NewGuid());
        Assert.True(lenient.RunUntil(() => allowed.State == ClientState.Lobby));
    }

    [Fact]
    public void Full_lobbies_and_running_games_turn_players_away()
    {
        using var net = LowLatency();
        var host = net.Join("Host");
        net.RunUntil(() => host.State == ClientState.Lobby);
        for (int i = 0; i < 7; i++)
            host.AddBot(i % 2 == 0 ? 1 : 0); // fills both teams to four
        net.Run(200);
        var late = net.Join("Late");
        Assert.True(net.RunUntil(() => late.State == ClientState.Rejected));
        Assert.Equal(RejectReason.LobbyFull, late.RejectReason);

        host.RemoveBot(1);
        host.StartGame();
        net.RunUntil(() => host.State == ClientState.InGame);
        var later = net.Join("Later");
        Assert.True(net.RunUntil(() => later.State == ClientState.Rejected));
        Assert.Equal(RejectReason.GameInProgress, later.RejectReason);
    }
}
