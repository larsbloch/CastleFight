using CastleFight.Net.Protocol;
using CastleFight.Sim;

namespace CastleFight.Net.Tests;

public class CodecTests
{
    public static TheoryData<Message> AllMessages => new()
    {
        new HelloMessage(ProtocolInfo.Version, Guid.NewGuid(), "Ærlig Øjvind"),
        new WelcomeMessage(5),
        new RejectedMessage(RejectReason.BuildMismatch, "nope"),
        new LobbyStateMessage(MapId.ThreeLanes, 2, [new LobbySlot(SlotKind.Human, 1, true, "A"), new LobbySlot(SlotKind.Empty, 0, false, "")]),
        new SetTeamMessage(1),
        new SetReadyMessage(true),
        new AddBotMessage(0),
        new RemoveBotMessage(3),
        new SetSlotTeamMessage(4, 1),
        new SetMapMessage(MapId.TwoLanes),
        new StartGameMessage(),
        new GameStartMessage(0xDEADBEEF12345678, MapId.TwoLanes, 3, 0, [new GamePlayer(0, 0, false), new GamePlayer(3, 1, true)]),
        new TickInputMessage(42, [Command.PlaceBuilding(1, UnitCatalog.Kennel, -3, 300), Command.DebugSpawn(1, UnitCatalog.Archer, 1000)]),
        new TickBundleMessage(43, [Command.PlaceBuilding(7, UnitCatalog.Barracks, short.MaxValue, short.MinValue)]),
        new StateHashMessage(60, ulong.MaxValue),
        new DesyncMessage(60, 64, [new SlotHash(0, 1), new SlotHash(2, 3)]),
        new PlayerLeftMessage(6),
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Every_message_survives_a_round_trip(Message message)
    {
        var decoded = Message.Decode(message.Encode());
        Assert.Equal(message, decoded);
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Truncated_or_padded_messages_are_rejected(Message message)
    {
        byte[] data = message.Encode();
        if (data.Length > 1)
            Assert.Throws<ProtocolException>(() => Message.Decode(data.AsSpan(0, data.Length - 1)));
        Assert.Throws<ProtocolException>(() => Message.Decode([.. data, 0]));
    }

    [Fact]
    public void Unknown_types_and_oversized_inputs_are_rejected()
    {
        Assert.Throws<ProtocolException>(() => Message.Decode([0xEE]));
        Assert.Throws<ProtocolException>(() => Message.Decode([]));

        var flood = new TickInputMessage(1, Enumerable.Repeat(Command.DebugSpawn(0, 5, 1), ProtocolInfo.MaxCommandsPerInput + 1).ToArray());
        Assert.Throws<ProtocolException>(() => Message.Decode(flood.Encode()));
    }

    [Fact]
    public void Long_names_are_cut_instead_of_breaking_the_message()
    {
        var hello = new HelloMessage(1, Guid.Empty, new string('x', 500));
        var decoded = (HelloMessage)Message.Decode(hello.Encode());
        Assert.Equal(ProtocolInfo.MaxStringBytes, decoded.Name.Length);
    }

    [Fact]
    public void A_command_is_twelve_bytes_on_the_wire()
    {
        int empty = new TickBundleMessage(1, []).Encode().Length;
        int one = new TickBundleMessage(1, [Command.PlaceBuilding(0, 1, 2, 3)]).Encode().Length;
        Assert.Equal(12, one - empty);
    }
}
