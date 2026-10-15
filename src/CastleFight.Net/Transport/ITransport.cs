namespace CastleFight.Net.Transport;

public enum TransportEventKind : byte
{
    Connected,
    Disconnected,
    Data,
}

/// <param name="Connection">Server side: the connection id. Client side: always 0.</param>
public readonly record struct TransportEvent(TransportEventKind Kind, int Connection, byte[]? Data = null, string? Reason = null);

/// <summary>
/// Reliable, ordered message transport, server side. Implemented with LiteNetLib for real games and in memory
/// (with simulated latency) for tests. Events are only delivered from <see cref="Poll"/>, on the caller's thread.
/// </summary>
public interface IServerTransport : IDisposable
{
    void Poll(List<TransportEvent> events);
    void Send(int connection, byte[] data);
    void Disconnect(int connection);
    int RoundTripMs(int connection);
}

/// <summary>Client side of <see cref="IServerTransport"/>.</summary>
public interface IClientTransport : IDisposable
{
    void Poll(List<TransportEvent> events);
    void Send(byte[] data);
    void Disconnect();
    int RoundTripMs { get; }
}
