using System.Security.Cryptography;
using CastleFight.Net.Protocol;
using CastleFight.Net.Transport;
using CastleFight.Sim;

namespace CastleFight.Net.Relay;

public enum RelayState
{
    Lobby,
    InGame,

    /// <summary>A desync was detected; no more bundles are sent.</summary>
    Halted,

    /// <summary>Every human left the game.</summary>
    Ended,
}

public sealed record RelayOptions
{
    public int InputDelayTicks { get; init; } = 3;
    public int HashIntervalTicks { get; init; } = Simulation.TicksPerSecond;

    /// <summary>How far ahead of the next bundle a client may send input.</summary>
    public int InputWindowTicks { get; init; } = 128;

    /// <summary>Development only: let different builds of the game play together.</summary>
    public bool AllowBuildMismatch { get; init; }

    /// <summary>Seed for the match. Defaults to a random one.</summary>
    public Func<ulong> SeedSource { get; init; } = () => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
}

/// <summary>
/// Lockstep relay. Never runs the simulation: it runs the lobby, then collects every player's
/// <see cref="TickInputMessage"/> and broadcasts one <see cref="TickBundleMessage"/> per tick once
/// everyone still connected has sent input for it. It also compares state hashes to detect desyncs.
/// Single-threaded: call <see cref="Poll"/> from one thread (see <see cref="RelayHost"/>).
/// </summary>
public sealed class RelayServer : IDisposable
{
    sealed class Slot
    {
        public SlotKind Kind;
        public int Connection = -1;
        public string Name = "";
        public byte Team;
        public bool Ready;
    }

    /// <summary>Inputs that have arrived for one tick, bucketed by the player id in each command.</summary>
    sealed class PendingTick
    {
        public readonly bool[] Received = new bool[GameSetup.MaxPlayers];
        public readonly List<Command>[] Commands = Enumerable.Range(0, GameSetup.MaxPlayers).Select(_ => new List<Command>()).ToArray();
    }

    readonly IServerTransport _transport;
    readonly RelayOptions _options;
    readonly List<TransportEvent> _events = new();
    readonly Slot[] _slots = Enumerable.Range(0, GameSetup.MaxPlayers).Select(_ => new Slot()).ToArray();
    readonly Dictionary<int, int> _slotOfConnection = new();
    readonly HashSet<int> _handshaking = new();
    readonly Dictionary<int, PendingTick> _pending = new();
    readonly SortedDictionary<int, ulong?[]> _hashes = new();
    readonly List<Command> _bundle = new();

    Guid? _lobbyBuild;
    MapId _map = MapId.SingleLane;
    int _hostSlot = -1;

    // In game:
    GameStartMessage? _start;
    int[] _connectionOfPlayer = [];
    bool[] _playerConnected = [];
    int _nextBundleTick;

    public RelayServer(IServerTransport transport, RelayOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new RelayOptions();
    }

    public RelayState State { get; private set; } = RelayState.Lobby;
    public int NextBundleTick => _nextBundleTick;
    public Action<string>? Log { get; set; }

    public void Poll()
    {
        _events.Clear();
        _transport.Poll(_events);
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case TransportEventKind.Connected:
                    _handshaking.Add(e.Connection);
                    break;
                case TransportEventKind.Disconnected:
                    OnDisconnected(e.Connection, e.Reason);
                    break;
                case TransportEventKind.Data:
                    Message message;
                    try
                    {
                        message = Message.Decode(e.Data);
                    }
                    catch (ProtocolException ex)
                    {
                        Log?.Invoke($"Dropping connection {e.Connection}: {ex.Message}");
                        _transport.Disconnect(e.Connection);
                        OnDisconnected(e.Connection, "protocol error");
                        continue;
                    }
                    OnMessage(e.Connection, message);
                    break;
            }
        }
    }

    public void Dispose() => _transport.Dispose();

    void OnMessage(int connection, Message message)
    {
        if (message is HelloMessage hello)
        {
            OnHello(connection, hello);
            return;
        }
        if (!_slotOfConnection.TryGetValue(connection, out int slot))
            return; // no Hello yet, or rejected

        if (State == RelayState.Lobby)
            OnLobbyMessage(slot, message);
        else if (State == RelayState.InGame)
            OnGameMessage(slot, message);
    }

    void OnHello(int connection, HelloMessage hello)
    {
        if (!_handshaking.Remove(connection))
            return;

        RejectReason? reason = null;
        string text = "";
        if (hello.ProtocolVersion != ProtocolInfo.Version)
            (reason, text) = (RejectReason.VersionMismatch, $"Server speaks protocol {ProtocolInfo.Version}, you have {hello.ProtocolVersion}. Update the game.");
        else if (State != RelayState.Lobby)
            (reason, text) = (RejectReason.GameInProgress, "A game is already running.");
        else if (_lobbyBuild is { } build && build != hello.BuildId && !_options.AllowBuildMismatch)
            (reason, text) = (RejectReason.BuildMismatch, "Your game build differs from the host's. Everyone needs the same version.");

        int slot = Array.FindIndex(_slots, s => s.Kind == SlotKind.Empty);
        byte team = SmallerTeam();
        if (reason is null && (slot < 0 || TeamCount(team) >= ProtocolInfo.TeamCapacity))
            (reason, text) = (RejectReason.LobbyFull, "The lobby is full.");

        if (reason is { } r)
        {
            Log?.Invoke($"Rejected '{hello.Name}': {r}");
            _transport.Send(connection, new RejectedMessage(r, text).Encode());
            return;
        }

        _lobbyBuild ??= hello.BuildId;
        string name = hello.Name.Trim();
        name = name.Length == 0 ? $"Player {slot + 1}" : name[..Math.Min(name.Length, ProtocolInfo.MaxNameLength)];
        var s = _slots[slot];
        s.Kind = SlotKind.Human;
        s.Connection = connection;
        s.Name = name;
        s.Team = team;
        s.Ready = false;
        _slotOfConnection[connection] = slot;
        if (_hostSlot < 0)
            _hostSlot = slot;

        Log?.Invoke($"'{name}' joined in slot {slot}, team {team}{(slot == _hostSlot ? " (host)" : "")}.");
        _transport.Send(connection, new WelcomeMessage((byte)slot).Encode());
        BroadcastLobby();
    }

    void OnLobbyMessage(int slot, Message message)
    {
        var self = _slots[slot];
        bool isHost = slot == _hostSlot;
        switch (message)
        {
            case SetTeamMessage m when m.Team < Simulation.TeamCount && m.Team != self.Team && TeamCount(m.Team) < ProtocolInfo.TeamCapacity:
                self.Team = m.Team;
                break;
            case SetReadyMessage m:
                self.Ready = m.Ready;
                break;
            case AddBotMessage m when isHost && m.Team < Simulation.TeamCount && TeamCount(m.Team) < ProtocolInfo.TeamCapacity:
                int free = Array.FindIndex(_slots, s => s.Kind == SlotKind.Empty);
                if (free < 0)
                    return;
                var bot = _slots[free];
                bot.Kind = SlotKind.Bot;
                bot.Connection = -1;
                bot.Team = m.Team;
                bot.Ready = true;
                bot.Name = $"Bot {free + 1}";
                break;
            case RemoveBotMessage m when isHost && m.Slot < _slots.Length && _slots[m.Slot].Kind == SlotKind.Bot:
                _slots[m.Slot].Kind = SlotKind.Empty;
                break;
            case SetSlotTeamMessage m when isHost && m.Slot < _slots.Length && _slots[m.Slot].Kind != SlotKind.Empty &&
                                           m.Team < Simulation.TeamCount && _slots[m.Slot].Team != m.Team &&
                                           TeamCount(m.Team) < ProtocolInfo.TeamCapacity:
                _slots[m.Slot].Team = m.Team;
                break;
            case SetMapMessage m when isHost && MapCatalog.IsValid(m.Map):
                _map = m.Map;
                break;
            case StartGameMessage when isHost && CanStart():
                StartGame();
                return;
            default:
                return; // not allowed or no change: nothing to broadcast
        }
        BroadcastLobby();
    }

    bool CanStart() => _slots.All(s => s.Kind != SlotKind.Human || s.Ready || Array.IndexOf(_slots, s) == _hostSlot);

    void StartGame()
    {
        var players = new List<GamePlayer>();
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            var s = _slots[slot];
            if (s.Kind != SlotKind.Empty)
                players.Add(new GamePlayer((byte)slot, s.Team, s.Kind == SlotKind.Bot));
        }

        _start = new GameStartMessage(_options.SeedSource(), _map, (byte)_options.InputDelayTicks, (byte)_hostSlot, players.ToArray());
        _connectionOfPlayer = players.Select(p => _slots[p.Slot].Kind == SlotKind.Human ? _slots[p.Slot].Connection : -1).ToArray();
        _playerConnected = _connectionOfPlayer.Select(c => c >= 0).ToArray();
        State = RelayState.InGame;
        Log?.Invoke($"Game started: {players.Count} players on {MapCatalog.DisplayName(_map)}, seed {_start.Seed}.");
        Broadcast(_start);

        // Nobody can issue commands for the first ticks (input is always scheduled InputDelay ahead).
        for (int tick = 0; tick < _options.InputDelayTicks; tick++)
            Broadcast(new TickBundleMessage(tick, []));
        _nextBundleTick = _options.InputDelayTicks;
    }

    void OnGameMessage(int slot, Message message)
    {
        int player = _start!.PlayerIdOfSlot(slot);
        if (player < 0 || !_playerConnected[player])
            return;

        switch (message)
        {
            case TickInputMessage input:
                OnInput(player, input);
                break;
            case StateHashMessage hash:
                OnHash(player, hash);
                break;
        }
    }

    void OnInput(int player, TickInputMessage input)
    {
        if (input.Tick < _nextBundleTick || input.Tick >= _nextBundleTick + _options.InputWindowTicks)
            return;
        if (!_pending.TryGetValue(input.Tick, out var pending))
            _pending[input.Tick] = pending = new PendingTick();
        if (pending.Received[player])
            return;

        pending.Received[player] = true;
        bool isHost = _start!.Players[player].Slot == _start.HostSlot;
        foreach (var command in input.Commands)
        {
            // A client may only command its own player; the host also commands the bots.
            int target = command.PlayerId;
            bool allowed = target == player ||
                           (isHost && (uint)target < (uint)_start.Players.Length && _start.Players[target].IsBot);
            if (allowed)
                pending.Commands[target].Add(command);
        }
        FlushBundles();
    }

    void FlushBundles()
    {
        while (State == RelayState.InGame)
        {
            _pending.TryGetValue(_nextBundleTick, out var pending);
            for (int player = 0; player < _playerConnected.Length; player++)
            {
                if (_playerConnected[player] && (pending is null || !pending.Received[player]))
                    return; // still waiting for someone
            }

            _bundle.Clear();
            if (pending is not null)
            {
                foreach (var commands in pending.Commands)
                    _bundle.AddRange(commands);
            }
            if (_bundle.Count > ProtocolInfo.MaxCommandsPerBundle)
                _bundle.RemoveRange(ProtocolInfo.MaxCommandsPerBundle, _bundle.Count - ProtocolInfo.MaxCommandsPerBundle);
            Broadcast(new TickBundleMessage(_nextBundleTick, _bundle.ToArray()));
            _pending.Remove(_nextBundleTick);
            _nextBundleTick++;
        }
    }

    void OnHash(int player, StateHashMessage message)
    {
        // A client can only hash ticks it has stepped, i.e. ticks we already bundled.
        if (message.Tick <= 0 || message.Tick > _nextBundleTick || message.Tick % _options.HashIntervalTicks != 0)
            return;
        if (!_hashes.TryGetValue(message.Tick, out var hashes))
            _hashes[message.Tick] = hashes = new ulong?[_playerConnected.Length];
        hashes[player] ??= message.Hash;
        CompareHashes();
    }

    void CompareHashes()
    {
        while (_hashes.Count > 0 && State == RelayState.InGame)
        {
            var (tick, hashes) = _hashes.First();
            ulong? reference = null;
            bool mismatch = false;
            for (int player = 0; player < hashes.Length; player++)
            {
                if (!_playerConnected[player])
                    continue;
                if (hashes[player] is not { } hash)
                    return; // the oldest check is still incomplete
                reference ??= hash;
                mismatch |= hash != reference;
            }
            _hashes.Remove(tick);

            if (mismatch)
            {
                var reported = Enumerable.Range(0, hashes.Length)
                    .Where(p => _playerConnected[p] && hashes[p].HasValue)
                    .Select(p => new SlotHash((byte)p, hashes[p]!.Value))
                    .ToArray();
                State = RelayState.Halted;
                Log?.Invoke($"DESYNC at tick {tick}: {string.Join(", ", reported.Select(h => $"player {h.PlayerId}={h.Hash:X16}"))}. Halted at tick {_nextBundleTick}.");
                Broadcast(new DesyncMessage(tick, _nextBundleTick, reported));
            }
        }
    }

    void OnDisconnected(int connection, string? reason)
    {
        _handshaking.Remove(connection);
        if (!_slotOfConnection.Remove(connection, out int slot))
            return;

        var s = _slots[slot];
        Log?.Invoke($"'{s.Name}' left ({reason}).");
        if (State == RelayState.Lobby)
        {
            s.Kind = SlotKind.Empty;
            s.Connection = -1;
            if (slot == _hostSlot)
            {
                _hostSlot = Array.FindIndex(_slots, x => x.Kind == SlotKind.Human);
                if (_hostSlot < 0)
                {
                    // Nobody human left: reset so the next player to join hosts a fresh lobby.
                    foreach (var other in _slots)
                        other.Kind = SlotKind.Empty;
                    _lobbyBuild = null;
                    _map = MapId.SingleLane;
                }
            }
            BroadcastLobby();
            return;
        }

        int player = _start!.PlayerIdOfSlot(slot);
        if (player < 0 || !_playerConnected[player])
            return;
        _playerConnected[player] = false;
        Broadcast(new PlayerLeftMessage((byte)player));

        if (!_playerConnected.Any(connected => connected))
        {
            State = RelayState.Ended;
            Log?.Invoke("Every player left; game over.");
            return;
        }
        FlushBundles(); // we may have been waiting for exactly this player
        CompareHashes();
    }

    void BroadcastLobby()
    {
        var slots = _slots.Select(s => new LobbySlot(s.Kind, s.Team, s.Ready, s.Kind == SlotKind.Empty ? "" : s.Name)).ToArray();
        Broadcast(new LobbyStateMessage(_map, (byte)Math.Max(_hostSlot, 0), slots));
    }

    void Broadcast(Message message)
    {
        byte[] data = message.Encode();
        if (State == RelayState.Lobby || _start is null)
        {
            foreach (var connection in _slotOfConnection.Keys.Order())
                _transport.Send(connection, data);
            return;
        }
        for (int player = 0; player < _connectionOfPlayer.Length; player++)
        {
            if (_playerConnected[player])
                _transport.Send(_connectionOfPlayer[player], data);
        }
    }

    int TeamCount(byte team) => _slots.Count(s => s.Kind != SlotKind.Empty && s.Team == team);

    byte SmallerTeam() => TeamCount(1) < TeamCount(0) ? (byte)1 : (byte)0;
}
