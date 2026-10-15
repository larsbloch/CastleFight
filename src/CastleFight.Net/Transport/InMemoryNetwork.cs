using CastleFight.Sim.FixedMath;

namespace CastleFight.Net.Transport;

/// <param name="MinLatencyMs">One-way latency range; every packet gets a random value in it.</param>
/// <param name="PreserveOrder">
/// True models LiteNetLib's reliable-ordered channel (a slow packet holds up the ones behind it).
/// False lets packets overtake each other.
/// </param>
public sealed record NetworkConditions(int MinLatencyMs = 0, int MaxLatencyMs = 0, bool PreserveOrder = true, ulong Seed = 1);

/// <summary>
/// Deterministic in-process network for tests: a virtual clock advanced by the test, and links with
/// configurable latency, jitter and reordering. Nothing is ever lost (the real channel is reliable).
/// </summary>
public sealed class InMemoryNetwork(NetworkConditions conditions)
{
    readonly Dictionary<int, Server> _servers = new();
    DeterministicRandom _random = new(conditions.Seed);
    long _sequence;

    public NetworkConditions Conditions { get; set; } = conditions;

    public long NowMs { get; private set; }

    public void Advance(int milliseconds) => NowMs += milliseconds;

    public IServerTransport Listen(int port)
    {
        var server = new Server(this);
        _servers.Add(port, server);
        return server;
    }

    public IClientTransport Connect(int port)
    {
        var client = new Client(this);
        if (_servers.TryGetValue(port, out var server))
            server.Accept(client);
        else
            client.Inbox.Push(this, new Packet(TransportEventKind.Disconnected, null, "No server on that port."));
        return client;
    }

    /// <summary>Simulates a crash or cable pull: both ends see a disconnect, nothing in flight arrives.</summary>
    public void Drop(IClientTransport transport)
    {
        var client = (Client)transport;
        client.Server?.Lose(client, "Timed out.");
        client.Lose("Timed out.");
    }

    int NextLatency()
    {
        var c = Conditions;
        return c.MinLatencyMs + (c.MaxLatencyMs > c.MinLatencyMs ? _random.NextInt(c.MaxLatencyMs - c.MinLatencyMs + 1) : 0);
    }

    sealed record Packet(TransportEventKind Kind, byte[]? Data, string? Reason = null);

    /// <summary>Packets in flight towards one endpoint.</summary>
    sealed class Queue
    {
        readonly List<(long DeliverAt, long Sequence, Packet Packet)> _inFlight = new();
        long _lastDeliverAt;

        public void Push(InMemoryNetwork network, Packet packet)
        {
            long deliverAt = network.NowMs + network.NextLatency();
            if (network.Conditions.PreserveOrder)
                deliverAt = Math.Max(deliverAt, _lastDeliverAt);
            _lastDeliverAt = Math.Max(_lastDeliverAt, deliverAt);
            _inFlight.Add((deliverAt, network._sequence++, packet));
        }

        public void TakeDue(long now, List<Packet> due)
        {
            _inFlight.Sort((a, b) => a.DeliverAt != b.DeliverAt ? a.DeliverAt.CompareTo(b.DeliverAt) : a.Sequence.CompareTo(b.Sequence));
            int count = 0;
            while (count < _inFlight.Count && _inFlight[count].DeliverAt <= now)
                due.Add(_inFlight[count++].Packet);
            _inFlight.RemoveRange(0, count);
        }

        public void Clear() => _inFlight.Clear();
    }

    sealed class Server(InMemoryNetwork network) : IServerTransport
    {
        readonly Dictionary<int, Client> _clients = new();
        readonly Dictionary<int, Queue> _inboxes = new();
        readonly List<Packet> _due = new();
        int _nextConnection = 1;

        public void Accept(Client client)
        {
            int connection = _nextConnection++;
            client.Server = this;
            client.Connection = connection;
            _clients.Add(connection, client);
            var inbox = new Queue();
            _inboxes.Add(connection, inbox);
            inbox.Push(network, new Packet(TransportEventKind.Connected, null));
            client.Inbox.Push(network, new Packet(TransportEventKind.Connected, null));
        }

        public void Receive(Client from, Packet packet)
        {
            if (_inboxes.TryGetValue(from.Connection, out var inbox))
                inbox.Push(network, packet);
        }

        public void Lose(Client client, string reason)
        {
            if (!_inboxes.TryGetValue(client.Connection, out var inbox))
                return;
            inbox.Clear();
            inbox.Push(network, new Packet(TransportEventKind.Disconnected, null, reason));
            _clients.Remove(client.Connection);
        }

        public void Poll(List<TransportEvent> events)
        {
            foreach (var (connection, inbox) in _inboxes.OrderBy(pair => pair.Key))
            {
                _due.Clear();
                inbox.TakeDue(network.NowMs, _due);
                foreach (var packet in _due)
                {
                    events.Add(new TransportEvent(packet.Kind, connection, packet.Data, packet.Reason));
                    if (packet.Kind == TransportEventKind.Disconnected)
                    {
                        _clients.Remove(connection);
                        inbox.Clear();
                        break;
                    }
                }
            }
        }

        public void Send(int connection, byte[] data)
        {
            if (_clients.TryGetValue(connection, out var client))
                client.Inbox.Push(network, new Packet(TransportEventKind.Data, data));
        }

        public void Disconnect(int connection)
        {
            if (!_clients.Remove(connection, out var client))
                return;
            client.Inbox.Push(network, new Packet(TransportEventKind.Disconnected, null, "Disconnected by server."));
            client.Server = null;
        }

        public int RoundTripMs(int connection) => network.Conditions.MinLatencyMs + network.Conditions.MaxLatencyMs;

        public void Dispose()
        {
            foreach (var client in _clients.Values.ToArray())
                client.Lose("Server stopped.");
            _clients.Clear();
        }
    }

    sealed class Client(InMemoryNetwork network) : IClientTransport
    {
        readonly List<Packet> _due = new();

        public Queue Inbox { get; } = new();
        public Server? Server { get; set; }
        public int Connection { get; set; }

        public int RoundTripMs => network.Conditions.MinLatencyMs + network.Conditions.MaxLatencyMs;

        public void Poll(List<TransportEvent> events)
        {
            _due.Clear();
            Inbox.TakeDue(network.NowMs, _due);
            foreach (var packet in _due)
                events.Add(new TransportEvent(packet.Kind, 0, packet.Data, packet.Reason));
        }

        public void Send(byte[] data) => Server?.Receive(this, new Packet(TransportEventKind.Data, data));

        public void Disconnect()
        {
            // Graceful: whatever is already in flight still arrives before the disconnect.
            Server?.Receive(this, new Packet(TransportEventKind.Disconnected, null, "Client left."));
            Server = null;
        }

        public void Lose(string reason)
        {
            Inbox.Clear();
            Inbox.Push(network, new Packet(TransportEventKind.Disconnected, null, reason));
            Server = null;
        }

        public void Dispose() => Disconnect();
    }
}
