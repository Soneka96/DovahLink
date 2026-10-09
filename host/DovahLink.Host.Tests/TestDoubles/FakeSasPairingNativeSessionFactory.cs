using DovahLink.Host.PairingCeremony;
using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Hands out one fake native session, or fails to open it, recording every open and its thread.</summary>
/// <param name="session">The session every successful open returns.</param>
internal sealed class FakeSasPairingNativeSessionFactory(FakeSasPairingNativeSession session) : ISasPairingNativeSessionFactory
{
    /// <summary>Backing field of <see cref="OpenCount"/>.</summary>
    private int openCount;

    /// <summary>The session every successful open returns.</summary>
    public FakeSasPairingNativeSession Session { get; } = session;

    /// <summary>When set, opening throws this classification.</summary>
    public NativeFailureKind? OpenFailure { get; set; }

    /// <summary>The number of open attempts.</summary>
    public int OpenCount => Volatile.Read(ref openCount);

    /// <summary>The managed thread ID of the last open.</summary>
    public int OpeningThread { get; private set; }

    /// <summary>The native library path of the last open.</summary>
    public string? OpenedPath { get; private set; }

    /// <summary>The authority scope of the last open.</summary>
    public byte[]? OpenedScope { get; private set; }

    /// <inheritdoc/>
    public ISasPairingNativeSession Open(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope)
    {
        Interlocked.Increment(ref openCount);
        OpeningThread = Environment.CurrentManagedThreadId;
        OpenedPath = nativeLibraryPath;
        OpenedScope = authorityScope.ToArray();
        return OpenFailure is NativeFailureKind failure
            ? throw new PairingCeremonyNativeException(failure, "Open", failure.ToString())
            : Session;
    }
}
