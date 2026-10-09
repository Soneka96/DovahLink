using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Delegates every call to the real session, timing drives.</summary>
/// <param name="inner">The real session.</param>
/// <param name="drives">Where drive timings are recorded.</param>
internal sealed class MeasuringNativeSession(ISasPairingNativeSession inner, ConcurrentQueue<(TimeSpan Duration, bool HadEvents)> drives) : ISasPairingNativeSession
{
    /// <inheritdoc/>
    public void AttachListener(Socket boundListener, CeremonyBootstrapFields localBootstrap) => inner.AttachListener(boundListener, localBootstrap);

    /// <inheritdoc/>
    public NativeDriveResult Drive()
    {
        var stopwatch = Stopwatch.StartNew();
        NativeDriveResult result = inner.Drive();
        drives.Enqueue((stopwatch.Elapsed, result.Events.Count > 0));
        return result;
    }

    /// <inheritdoc/>
    public void AuthorizeExposure(NativeRunHandle run) => inner.AuthorizeExposure(run);

    /// <inheritdoc/>
    public void ExposeKey(NativeRunHandle run) => inner.ExposeKey(run);

    /// <inheritdoc/>
    public NativeSasPresentation? Presentation(NativeRunHandle run) => inner.Presentation(run);

    /// <inheritdoc/>
    public void ApproveSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => inner.ApproveSas(run, ceremonyIdentity);

    /// <inheritdoc/>
    public void EmitBootstrapMac(NativeRunHandle run) => inner.EmitBootstrapMac(run);

    /// <inheritdoc/>
    public void RejectSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => inner.RejectSas(run, ceremonyIdentity);

    /// <inheritdoc/>
    public void CancelSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => inner.CancelSas(run, ceremonyIdentity);

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();
}
