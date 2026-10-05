using System.Net;
using System.Net.Sockets;

namespace DovahLink.Host.PairingCeremony.TestPeer;

/// <summary>
/// A byte-transparent loopback relay joining the peer's own listener to the Host's pairing listener,
/// because the sas-pairing native ABI only creates connections from a listener's accept. It copies
/// bytes unchanged in both directions and interprets nothing.
/// </summary>
internal sealed class ByteRelay : IDisposable
{
    /// <summary>The connection to the peer's own listener.</summary>
    private readonly TcpClient towardPeer;

    /// <summary>The connection to the Host's pairing listener.</summary>
    private readonly TcpClient towardHost;

    /// <summary>The two copy loops.</summary>
    private readonly Task[] pumps;

    /// <summary>Connects both ends and starts copying.</summary>
    /// <param name="peerPort">The loopback port of the peer's own listener.</param>
    /// <param name="hostPort">The loopback port of the Host's pairing listener.</param>
    public ByteRelay(int peerPort, int hostPort)
    {
        towardHost = new TcpClient();
        towardHost.Connect(IPAddress.Loopback, hostPort);
        towardPeer = new TcpClient();
        towardPeer.Connect(IPAddress.Loopback, peerPort);
        towardHost.NoDelay = true;
        towardPeer.NoDelay = true;
        pumps =
        [
            Task.Run(() => Copy(towardPeer.GetStream(), towardHost.GetStream())),
            Task.Run(() => Copy(towardHost.GetStream(), towardPeer.GetStream())),
        ];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        towardPeer.Dispose();
        towardHost.Dispose();
        Task.WaitAll(pumps, TimeSpan.FromSeconds(5));
    }

    /// <summary>Copies bytes until either side closes or fails.</summary>
    /// <param name="from">The source stream.</param>
    /// <param name="to">The destination stream.</param>
    private static void Copy(Stream from, Stream to)
    {
        try
        {
            from.CopyTo(to);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // One side closed; the relay simply stops.
        }
    }
}
