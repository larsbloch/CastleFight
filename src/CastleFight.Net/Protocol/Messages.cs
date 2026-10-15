using CastleFight.Sim;

namespace CastleFight.Net.Protocol;

public static class ProtocolInfo
{
    /// <summary>Bump on any wire-format or simulation-rule change; mismatched clients are rejected.</summary>
    public const ushort Version = 1;

    public const int DefaultPort = 7777;
    public const string ConnectionKey = "CastleFight";
    public const int MaxStringBytes = 48;
    public const int MaxNameLength = 24;
    public const int MaxCommandsPerInput = 64;
    public const int MaxCommandsPerBundle = 1024;
    public const int TeamCapacity = GameSetup.MaxPlayers / Simulation.TeamCount;

    /// <summary>
    /// Identifies the exact simulation build. Clients of one lobby must match the first player's build,
    /// because two different builds of the sim are not guaranteed to stay in sync.
    /// </summary>
    public static Guid BuildId { get; } = typeof(Simulation).Assembly.ManifestModule.ModuleVersionId;
}

public enum MessageType : byte
{
    Hello = 1,
    Welcome,
    Rejected,
    LobbyState,
    SetTeam,
    SetReady,
    AddBot,
    RemoveBot,
    SetSlotTeam,
    SetMap,
    StartGame,
    GameStart,
    TickInput,
    TickBundle,
    StateHash,
    Desync,
    PlayerLeft,
}

public enum RejectReason : byte
{
    VersionMismatch = 1,
    BuildMismatch,
    LobbyFull,
    GameInProgress,
}

public enum SlotKind : byte
{
    Empty,
    Human,
    Bot,
}

public readonly record struct LobbySlot(SlotKind Kind, byte Team, bool Ready, string Name);

/// <summary>A player in a started game. The player id is its index in <see cref="GameStartMessage.Players"/>.</summary>
public readonly record struct GamePlayer(byte Slot, byte Team, bool IsBot);

public readonly record struct SlotHash(byte PlayerId, ulong Hash);

/// <summary>Every message on the wire: one type byte, then the payload.</summary>
public abstract record Message
{
    public abstract MessageType Type { get; }

    protected abstract void WritePayload(NetWriter writer);

    public byte[] Encode()
    {
        var writer = new NetWriter();
        writer.WriteByte((byte)Type);
        WritePayload(writer);
        return writer.ToArray();
    }

    public static Message Decode(ReadOnlySpan<byte> data)
    {
        var reader = new NetReader(data);
        var type = (MessageType)reader.ReadByte();
        Message message = type switch
        {
            MessageType.Hello => new HelloMessage(reader.ReadUInt16(), reader.ReadGuid(), reader.ReadString()),
            MessageType.Welcome => new WelcomeMessage(reader.ReadByte()),
            MessageType.Rejected => new RejectedMessage((RejectReason)reader.ReadByte(), reader.ReadString()),
            MessageType.LobbyState => LobbyStateMessage.Read(ref reader),
            MessageType.SetTeam => new SetTeamMessage(reader.ReadByte()),
            MessageType.SetReady => new SetReadyMessage(reader.ReadBool()),
            MessageType.AddBot => new AddBotMessage(reader.ReadByte()),
            MessageType.RemoveBot => new RemoveBotMessage(reader.ReadByte()),
            MessageType.SetSlotTeam => new SetSlotTeamMessage(reader.ReadByte(), reader.ReadByte()),
            MessageType.SetMap => new SetMapMessage((MapId)reader.ReadByte()),
            MessageType.StartGame => new StartGameMessage(),
            MessageType.GameStart => GameStartMessage.Read(ref reader),
            MessageType.TickInput => new TickInputMessage(reader.ReadInt32(), ReadCommands(ref reader, ProtocolInfo.MaxCommandsPerInput)),
            MessageType.TickBundle => new TickBundleMessage(reader.ReadInt32(), ReadCommands(ref reader, ProtocolInfo.MaxCommandsPerBundle)),
            MessageType.StateHash => new StateHashMessage(reader.ReadInt32(), reader.ReadUInt64()),
            MessageType.Desync => DesyncMessage.Read(ref reader),
            MessageType.PlayerLeft => new PlayerLeftMessage(reader.ReadByte()),
            _ => throw new ProtocolException($"Unknown message type {(byte)type}."),
        };
        reader.ExpectEnd();
        return message;
    }

    /// <summary>12 bytes per command, all integers.</summary>
    internal static void WriteCommands(NetWriter writer, IReadOnlyList<Command> commands)
    {
        writer.WriteUInt16((ushort)commands.Count);
        foreach (var command in commands)
        {
            writer.WriteByte((byte)command.PlayerId);
            writer.WriteByte((byte)command.Kind);
            writer.WriteUInt16(command.TypeId);
            writer.WriteInt16(command.TileX);
            writer.WriteInt16(command.TileY);
            writer.WriteInt32(command.Count);
        }
    }

    internal static Command[] ReadCommands(ref NetReader reader, int max)
    {
        int count = reader.ReadUInt16();
        if (count > max)
            throw new ProtocolException($"{count} commands is more than the limit of {max}.");
        var commands = new Command[count];
        for (int i = 0; i < count; i++)
        {
            commands[i] = new Command(reader.ReadByte(), (CommandKind)reader.ReadByte(), reader.ReadUInt16(),
                reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt32());
        }
        return commands;
    }
}

/// <summary>C→S, first message after connecting.</summary>
public sealed record HelloMessage(ushort ProtocolVersion, Guid BuildId, string Name) : Message
{
    public override MessageType Type => MessageType.Hello;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteGuid(BuildId);
        writer.WriteString(Name);
    }
}

public sealed record WelcomeMessage(byte Slot) : Message
{
    public override MessageType Type => MessageType.Welcome;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte(Slot);
}

public sealed record RejectedMessage(RejectReason Reason, string Text) : Message
{
    public override MessageType Type => MessageType.Rejected;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteByte((byte)Reason);
        writer.WriteString(Text);
    }
}

/// <summary>S→all whenever anything in the lobby changes.</summary>
public sealed record LobbyStateMessage(MapId Map, byte HostSlot, LobbySlot[] Slots) : Message
{
    public override MessageType Type => MessageType.LobbyState;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteByte((byte)Map);
        writer.WriteByte(HostSlot);
        writer.WriteByte((byte)Slots.Length);
        foreach (var slot in Slots)
        {
            writer.WriteByte((byte)slot.Kind);
            writer.WriteByte(slot.Team);
            writer.WriteBool(slot.Ready);
            writer.WriteString(slot.Name);
        }
    }

    internal static LobbyStateMessage Read(ref NetReader reader)
    {
        var map = (MapId)reader.ReadByte();
        byte host = reader.ReadByte();
        int count = reader.ReadByte();
        if (count > GameSetup.MaxPlayers)
            throw new ProtocolException("Too many lobby slots.");
        var slots = new LobbySlot[count];
        for (int i = 0; i < count; i++)
            slots[i] = new LobbySlot((SlotKind)reader.ReadByte(), reader.ReadByte(), reader.ReadBool(), reader.ReadString());
        return new LobbyStateMessage(map, host, slots);
    }

    public bool Equals(LobbyStateMessage? other) =>
        other is not null && Map == other.Map && HostSlot == other.HostSlot && Slots.SequenceEqual(other.Slots);

    public override int GetHashCode() => HashCode.Combine(Map, HostSlot, Slots.Length);
}

public sealed record SetTeamMessage(byte Team) : Message
{
    public override MessageType Type => MessageType.SetTeam;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte(Team);
}

public sealed record SetReadyMessage(bool Ready) : Message
{
    public override MessageType Type => MessageType.SetReady;
    protected override void WritePayload(NetWriter writer) => writer.WriteBool(Ready);
}

/// <summary>Host only.</summary>
public sealed record AddBotMessage(byte Team) : Message
{
    public override MessageType Type => MessageType.AddBot;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte(Team);
}

/// <summary>Host only.</summary>
public sealed record RemoveBotMessage(byte Slot) : Message
{
    public override MessageType Type => MessageType.RemoveBot;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte(Slot);
}

/// <summary>Host only: move any player or bot to a team.</summary>
public sealed record SetSlotTeamMessage(byte Slot, byte Team) : Message
{
    public override MessageType Type => MessageType.SetSlotTeam;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteByte(Slot);
        writer.WriteByte(Team);
    }
}

/// <summary>Host only.</summary>
public sealed record SetMapMessage(MapId Map) : Message
{
    public override MessageType Type => MessageType.SetMap;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte((byte)Map);
}

/// <summary>Host only.</summary>
public sealed record StartGameMessage : Message
{
    public override MessageType Type => MessageType.StartGame;
    protected override void WritePayload(NetWriter writer) { }
}

/// <summary>S→all. Everyone builds the same <see cref="GameSetup"/> from this.</summary>
public sealed record GameStartMessage(ulong Seed, MapId Map, byte InputDelayTicks, byte HostSlot, GamePlayer[] Players) : Message
{
    public override MessageType Type => MessageType.GameStart;

    public GameSetup ToSetup() => new(Seed, Players.Select(p => p.Team).ToArray()) { Map = Map };

    /// <summary>Player id of the lobby slot, or -1.</summary>
    public int PlayerIdOfSlot(int slot) => Array.FindIndex(Players, p => p.Slot == slot);

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteUInt64(Seed);
        writer.WriteByte((byte)Map);
        writer.WriteByte(InputDelayTicks);
        writer.WriteByte(HostSlot);
        writer.WriteByte((byte)Players.Length);
        foreach (var player in Players)
        {
            writer.WriteByte(player.Slot);
            writer.WriteByte(player.Team);
            writer.WriteBool(player.IsBot);
        }
    }

    internal static GameStartMessage Read(ref NetReader reader)
    {
        ulong seed = reader.ReadUInt64();
        var map = (MapId)reader.ReadByte();
        byte delay = reader.ReadByte();
        byte host = reader.ReadByte();
        int count = reader.ReadByte();
        if (count > GameSetup.MaxPlayers)
            throw new ProtocolException("Too many players.");
        var players = new GamePlayer[count];
        for (int i = 0; i < count; i++)
            players[i] = new GamePlayer(reader.ReadByte(), reader.ReadByte(), reader.ReadBool());
        return new GameStartMessage(seed, map, delay, host, players);
    }

    public bool Equals(GameStartMessage? other) =>
        other is not null && Seed == other.Seed && Map == other.Map && InputDelayTicks == other.InputDelayTicks &&
        HostSlot == other.HostSlot && Players.SequenceEqual(other.Players);

    public override int GetHashCode() => HashCode.Combine(Seed, Map, Players.Length);
}

/// <summary>C→S for every tick, even when empty: the relay waits for everyone's input before bundling.</summary>
public sealed record TickInputMessage(int Tick, Command[] Commands) : Message
{
    public override MessageType Type => MessageType.TickInput;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteInt32(Tick);
        WriteCommands(writer, Commands);
    }

    public bool Equals(TickInputMessage? other) => other is not null && Tick == other.Tick && Commands.SequenceEqual(other.Commands);
    public override int GetHashCode() => HashCode.Combine(Tick, Commands.Length);
}

/// <summary>S→all: every player's commands for one tick, in canonical order (player id, then issue order).</summary>
public sealed record TickBundleMessage(int Tick, Command[] Commands) : Message
{
    public override MessageType Type => MessageType.TickBundle;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteInt32(Tick);
        WriteCommands(writer, Commands);
    }

    public bool Equals(TickBundleMessage? other) => other is not null && Tick == other.Tick && Commands.SequenceEqual(other.Commands);
    public override int GetHashCode() => HashCode.Combine(Tick, Commands.Length);
}

/// <summary>C→S: hash of the state after <see cref="Tick"/> ticks.</summary>
public sealed record StateHashMessage(int Tick, ulong Hash) : Message
{
    public override MessageType Type => MessageType.StateHash;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteInt32(Tick);
        writer.WriteUInt64(Hash);
    }
}

/// <summary>
/// S→all: hashes for <see cref="Tick"/> differ. The relay stops bundling; every client steps to exactly
/// <see cref="DumpTick"/> (no client can be past it) and dumps its state, so the dumps can be diffed.
/// </summary>
public sealed record DesyncMessage(int Tick, int DumpTick, SlotHash[] Hashes) : Message
{
    public override MessageType Type => MessageType.Desync;

    protected override void WritePayload(NetWriter writer)
    {
        writer.WriteInt32(Tick);
        writer.WriteInt32(DumpTick);
        writer.WriteByte((byte)Hashes.Length);
        foreach (var hash in Hashes)
        {
            writer.WriteByte(hash.PlayerId);
            writer.WriteUInt64(hash.Hash);
        }
    }

    internal static DesyncMessage Read(ref NetReader reader)
    {
        int tick = reader.ReadInt32();
        int dumpTick = reader.ReadInt32();
        int count = reader.ReadByte();
        if (count > GameSetup.MaxPlayers)
            throw new ProtocolException("Too many hashes.");
        var hashes = new SlotHash[count];
        for (int i = 0; i < count; i++)
            hashes[i] = new SlotHash(reader.ReadByte(), reader.ReadUInt64());
        return new DesyncMessage(tick, dumpTick, hashes);
    }

    public bool Equals(DesyncMessage? other) =>
        other is not null && Tick == other.Tick && DumpTick == other.DumpTick && Hashes.SequenceEqual(other.Hashes);

    public override int GetHashCode() => HashCode.Combine(Tick, DumpTick);
}

/// <summary>S→all: the player disconnected. Their buildings keep working; they just send no more commands.</summary>
public sealed record PlayerLeftMessage(byte PlayerId) : Message
{
    public override MessageType Type => MessageType.PlayerLeft;
    protected override void WritePayload(NetWriter writer) => writer.WriteByte(PlayerId);
}
