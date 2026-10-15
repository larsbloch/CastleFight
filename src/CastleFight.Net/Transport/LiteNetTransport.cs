using CastleFight.Net.Protocol;
using LiteNetLib;

namespace CastleFight.Net.Transport;

/// <summary>UDP server on LiteNetLib's reliable-ordered channel.</summary>
public sealed class LiteNetServerTransport : IServerTransport
{
    const int MaxConnections = 16; // 8 players plus room for rejected/handshaking peers

    readonly EventBasedNetListener _listener = new();
    readonly NetManager _manager;
    readonly Dictionary<int, NetPeer> _peers = new();
    readonly List<TransportEvent> _pending = new();

    public LiteNetServerTransport(int port)
    {
        _manager = new NetManager(_listener) { DisconnectTimeout = 10_000, AutoRecycle = true };
        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount < MaxConnections)
                request.AcceptIfKey(ProtocolInfo.ConnectionKey);
            else
                request.Reject();
        };
        _listener.PeerConnectedEvent += peer =>
        {
            _peers[peer.Id] = peer;
            _pending.Add(new TransportEvent(TransportEventKind.Connected, peer.Id));
        };
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            _peers.Remove(peer.Id);
            _pending.Add(new TransportEvent(TransportEventKind.Disconnected, peer.Id, Reason: info.Reason.ToString()));
        };
        _listener.NetworkReceiveEvent += (peer, reader, _, _) =>
            _pending.Add(new TransportEvent(TransportEventKind.Data, peer.Id, reader.GetRemainingBytes()));

        if (!_manager.Start(port))
            throw new InvalidOperationException($"Could not listen on UDP port {port}. Is another game already hosting on it?");
    }

    public int Port => _manager.LocalPort;

    public void Poll(List<TransportEvent> events)
    {
        _manager.PollEvents();
        events.AddRange(_pending);
        _pending.Clear();
    }

    public void Send(int connection, byte[] data)
    {
        if (_peers.TryGetValue(connection, out var peer))
            peer.Send(data, DeliveryMethod.ReliableOrdered);
    }

    public void Disconnect(int connection)
    {
        if (_peers.TryGetValue(connection, out var peer))
            _manager.DisconnectPeer(peer);
    }

    public int RoundTripMs(int connection) => _peers.TryGetValue(connection, out var peer) ? peer.RoundTripTime : 0;

    public void Dispose() => _manager.Stop();
}

/// <summary>Client side of <see cref="LiteNetServerTransport"/>.</summary>
public sealed class LiteNetClientTransport : IClientTransport
{
    readonly EventBasedNetListener _listener = new();
    readonly NetManager _manager;
    readonly List<TransportEvent> _pending = new();
    NetPeer? _peer;

    public LiteNetClientTransport(string host, int port)
    {
        _manager = new NetManager(_listener) { DisconnectTimeout = 10_000, AutoRecycle = true };
        _listener.PeerConnectedEvent += peer =>
        {
            _peer = peer;
            _pending.Add(new TransportEvent(TransportEventKind.Connected, 0));
        };
        _listener.PeerDisconnectedEvent += (_, info) =>
        {
            _peer = null;
            _pending.Add(new TransportEvent(TransportEventKind.Disconnected, 0, Reason: info.Reason.ToString()));
        };
        _listener.NetworkReceiveEvent += (_, reader, _, _) =>
            _pending.Add(new TransportEvent(TransportEventKind.Data, 0, reader.GetRemainingBytes()));

        _manager.Start();
        _manager.Connect(host, port, ProtocolInfo.ConnectionKey);
    }

    public int RoundTripMs => _peer?.RoundTripTime ?? 0;

    public void Poll(List<TransportEvent> events)
    {
        _manager.PollEvents();
        events.AddRange(_pending);
        _pending.Clear();
    }

    public void Send(byte[] data) => _peer?.Send(data, DeliveryMethod.ReliableOrdered);

    public void Disconnect() => _manager.DisconnectAll();

    public void Dispose() => _manager.Stop();
}
