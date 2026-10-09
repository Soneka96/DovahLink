using System.Collections.Concurrent;
using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// Opens real native sessions and wraps each in a decorator that times every drive, so the real-native
/// tests can measure the drive bound and the owner's idle cadence without changing production code.
/// </summary>
internal sealed class MeasuringNativeSessionFactory : ISasPairingNativeSessionFactory
{
    /// <summary>The real factory.</summary>
    private readonly SasPairingNativeSessionFactory inner = new();

    /// <summary>Every drive's duration and whether it returned events, in order.</summary>
    public ConcurrentQueue<(TimeSpan Duration, bool HadEvents)> Drives { get; } = new();

    /// <summary>The real native session opened for the measured Host, or <see langword="null"/> before startup.</summary>
    public SasPairingNativeSession? OpenedSession { get; private set; }

    /// <inheritdoc/>
    public ISasPairingNativeSession Open(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope)
    {
        OpenedSession = (SasPairingNativeSession)inner.Open(nativeLibraryPath, authorityScope);
        return new MeasuringNativeSession(OpenedSession, Drives);
    }
}
