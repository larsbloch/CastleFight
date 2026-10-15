using CastleFight.Net.Protocol;
using CastleFight.Net.Transport;
using CastleFight.Sim;

namespace CastleFight.Net.Client;

public enum ClientState
{
    Connecting,
    Lobby,
    InGame,
    Rejected,
    Disconnected,
}

/// <summary>
/// The game's end of the relay protocol: handshake, lobby state and actions, buffering of received tick
/// bundles, and sending local input and state hashes. Knows nothing about the simulation itself; see
/// <c>NetworkSession</c> for the part that steps it. Call <see cref="Poll"/> once per frame.
/// </summary>
public sealed class LockstepClient : IDisposable
{
    readonly IClientTransport _transport;
    readonly List<TransportEvent> _events = new();
    readonly Dictionary<int, Command[]> _bundles = new();
    readonly ushort _protocolVersion;
    readonly Guid _buildId;
    bool _ended;

    public LockstepClient(IClientTransport transport, string playerName, ushort? protocolVersion = null, Guid? buildId = null)
    {
        _transport = transport;
        PlayerName = playerName;
        _protocolVersion = protocolVersion ?? ProtocolInfo.Version;
        _buildId = buildId ?? ProtocolInfo.BuildId;
    }

    public string PlayerName { get; }
    public ClientState State { get; private set; } = ClientState.Connecting;

    /// <summary>Why the connection ended (rejection text or transport reason).</summary>
    public string? EndReason { get; private set; }

    public RejectReason? RejectReason { get; private set; }
    public int LocalSlot { get; private set; } = -1;
    public LobbyStateMessage? Lobby { get; private set; }
    public GameStartMessage? Start { get; private set; }
    public bool IsHost => Start is { } start ? start.HostSlot == LocalSlot : Lobby?.HostSlot == LocalSlot;
    public int LocalPlayerId => Start?.PlayerIdOfSlot(LocalSlot) ?? -1;
    public int RoundTripMs => _transport.RoundTripMs;

    internal IClientTransport Transport => _transport;

    /// <summary>Set when the relay reported a desync: step to exactly this tick, then dump the state.</summary>
    public DesyncMessage? Desync { get; private set; }

    public event Action<LobbyStateMessage>? LobbyChanged;
    public event Action<GameStartMessage>? GameStarted;
    public event Action<int>? PlayerLeft;
    public event Action<DesyncMessage>? DesyncDetected;

    /// <summary>Fires once when the connection ends for any reason (see <see cref="EndReason"/>).</summary>
    public event Action<string>? Disconnected;

    public void Poll()
    {
        _events.Clear();
        _transport.Poll(_events);
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case TransportEventKind.Connected:
                    _transport.Send(new HelloMessage(_protocolVersion, _buildId, PlayerName).Encode());
                    break;
                case TransportEventKind.Disconnected:
                    End(ClientState.Disconnected, e.Reason ?? "Disconnected.");
                    break;
                case TransportEventKind.Data:
                    try
                    {
                        OnMessage(Message.Decode(e.Data));
                    }
                    catch (ProtocolException ex)
                    {
                        _transport.Disconnect();
                        End(ClientState.Disconnected, $"Bad message from server: {ex.Message}");
                    }
                    break;
            }
        }
    }

    void OnMessage(Message message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                LocalSlot = welcome.Slot;
                State = ClientState.Lobby;
                break;
            case RejectedMessage rejected:
                RejectReason = rejected.Reason;
                EndReason = rejected.Text;
                _transport.Disconnect();
                End(ClientState.Rejected, rejected.Text);
                break;
            case LobbyStateMessage lobby:
                Lobby = lobby;
                LobbyChanged?.Invoke(lobby);
                break;
            case GameStartMessage start:
                Start = start;
                State = ClientState.InGame;
                GameStarted?.Invoke(start);
                break;
            case TickBundleMessage bundle:
                _bundles.TryAdd(bundle.Tick, bundle.Commands);
                break;
            case PlayerLeftMessage left:
                PlayerLeft?.Invoke(left.PlayerId);
                break;
            case DesyncMessage desync:
                Desync ??= desync;
                DesyncDetected?.Invoke(desync);
                break;
        }
    }

    void End(ClientState state, string reason)
    {
        if (_ended)
            return;
        _ended = true;
        State = state;
        EndReason = reason;
        Disconnected?.Invoke(reason);
    }

    // ---- Lobby actions ----

    public void SetTeam(int team) => Send(new SetTeamMessage((byte)team));
    public void SetReady(bool ready) => Send(new SetReadyMessage(ready));
    public void AddBot(int team) => Send(new AddBotMessage((byte)team));
    public void RemoveBot(int slot) => Send(new RemoveBotMessage((byte)slot));
    public void SetSlotTeam(int slot, int team) => Send(new SetSlotTeamMessage((byte)slot, (byte)team));
    public void SetMap(MapId map) => Send(new SetMapMessage(map));
    public void StartGame() => Send(new StartGameMessage());

    // ---- Game ----

    public bool HasBundle(int tick) => _bundles.ContainsKey(tick);

    /// <summary>How many consecutive bundles from <paramref name="tick"/> on have arrived.</summary>
    public int BufferedFrom(int tick)
    {
        int count = 0;
        while (_bundles.ContainsKey(tick + count))
            count++;
        return count;
    }

    /// <summary>Removes and returns the commands for <paramref name="tick"/>.</summary>
    public bool TryTakeBundle(int tick, out Command[] commands) => _bundles.Remove(tick, out commands!);

    public void SendInput(int tick, IReadOnlyList<Command> commands)
    {
        int count = Math.Min(commands.Count, ProtocolInfo.MaxCommandsPerInput);
        Send(new TickInputMessage(tick, commands.Take(count).ToArray()));
    }

    public void SendHash(int tick, ulong hash) => Send(new StateHashMessage(tick, hash));

    public void Leave()
    {
        _transport.Disconnect();
        End(ClientState.Disconnected, "You left.");
    }

    public void Dispose() => _transport.Dispose();

    void Send(Message message)
    {
        if (State is ClientState.Lobby or ClientState.InGame)
            _transport.Send(message.Encode());
    }
}
