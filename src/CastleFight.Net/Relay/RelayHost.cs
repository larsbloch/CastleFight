using CastleFight.Net.Transport;

namespace CastleFight.Net.Relay;

/// <summary>
/// Runs a <see cref="RelayServer"/> on its own thread, so relay latency never depends on the host's
/// frame rate. Used both when a player hosts from the game and by the standalone server.
/// </summary>
public sealed class RelayHost : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly Thread _thread;

    public RelayHost(int port, RelayOptions? options = null, Action<string>? log = null)
    {
        // Bind here so a busy port fails on the caller's thread with a clear exception.
        var transport = new LiteNetServerTransport(port);
        Server = new RelayServer(transport, options) { Log = log };
        Port = transport.Port;
        _thread = new Thread(Run) { IsBackground = true, Name = "CastleFight relay" };
        _thread.Start();
    }

    public RelayServer Server { get; }
    public int Port { get; }

    /// <summary>Set if the relay thread crashed.</summary>
    public Exception? Error { get; private set; }

    void Run()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                Server.Poll();
                Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            Error = ex;
            Server.Log?.Invoke($"Relay crashed: {ex}");
        }
        finally
        {
            Server.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
