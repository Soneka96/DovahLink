using System.Net.Sockets;
using SasPairing;

namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>
/// One opened sas-pairing runtime, registered authority, and native host, used only by the
/// owner thread of one <see cref="PairingCeremonyHost"/>. It speaks DovahLink-owned values only, and
/// every failure surfaces as a classified <see cref="PairingCeremonyNativeException"/>.
/// </summary>
internal interface ISasPairingNativeSession : IDisposable
{
    /// <summary>Hands an already bound and listening socket to the native host with this Host's Bootstrap.</summary>
    /// <param name="boundListener">The caller's bound, listening socket; ownership moves to the native library.</param>
    /// <param name="localBootstrap">This Host's Responder Bootstrap.</param>
    /// <exception cref="PairingCeremonyNativeException">Adoption or attachment failed.</exception>
    void AttachListener(Socket boundListener, CeremonyBootstrapFields localBootstrap);

    /// <summary>Runs one bounded native drive and translates its events; may block for the native bound.</summary>
    /// <returns>The translated events and any owner-loop failure.</returns>
    /// <exception cref="PairingCeremonyNativeException">The drive call itself failed.</exception>
    NativeDriveResult Drive();

    /// <summary>Records this Host's fresh, explicit local authorization to expose its key in a run.</summary>
    /// <param name="run">The run.</param>
    /// <exception cref="PairingCeremonyNativeException">The step failed.</exception>
    void AuthorizeExposure(NativeRunHandle run);

    /// <summary>Exposes this Host's ephemeral key in a run; the security-spending step.</summary>
    /// <param name="run">The run.</param>
    /// <exception cref="PairingCeremonyNativeException">The step failed.</exception>
    void ExposeKey(NativeRunHandle run);

    /// <summary>Reads a run's SAS presentation without changing anything.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The presentation, or <see langword="null"/> while no SAS is live.</returns>
    /// <exception cref="PairingCeremonyNativeException">The read failed.</exception>
    NativeSasPresentation? Presentation(NativeRunHandle run);

    /// <summary>Records MATCH for a run's presented ceremony identity; emits nothing.</summary>
    /// <param name="run">The run.</param>
    /// <param name="ceremonyIdentity">The exact ceremony identity of the run's current presentation.</param>
    /// <exception cref="PairingCeremonyNativeException">The identity is not the presented one, or the step failed.</exception>
    void ApproveSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity);

    /// <summary>Emits this Host's Bootstrap MAC after MATCH.</summary>
    /// <param name="run">The run.</param>
    /// <exception cref="PairingCeremonyNativeException">The step failed.</exception>
    void EmitBootstrapMac(NativeRunHandle run);

    /// <summary>Records MISMATCH for a run's presented ceremony identity, ending the run.</summary>
    /// <param name="run">The run.</param>
    /// <param name="ceremonyIdentity">The exact ceremony identity of the run's current presentation.</param>
    /// <exception cref="PairingCeremonyNativeException">The identity is not the presented one, or the step failed.</exception>
    void RejectSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity);

    /// <summary>Records CANCEL for a run's presented ceremony identity, ending the run.</summary>
    /// <param name="run">The run.</param>
    /// <param name="ceremonyIdentity">The exact ceremony identity of the run's current presentation.</param>
    /// <exception cref="PairingCeremonyNativeException">The identity is not the presented one, or the step failed.</exception>
    void CancelSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity);
}

/// <inheritdoc cref="ISasPairingNativeSession"/>
/// <remarks>
/// Each local result is read exactly once and released immediately, inside <see cref="Drive"/>, so no
/// live native result ever leaves this type; connections the package flags are disposed after the
/// whole batch, as the package requires. Disposal releases the host (which closes its listener and
/// connections), then the authority, then the runtime; the native parents' own cleanup ends their
/// children, so nothing is disposed twice.
/// </remarks>
internal sealed class SasPairingNativeSession : ISasPairingNativeSession
{
    /// <summary>The loaded runtime.</summary>
    private readonly SasPairingRuntime runtime;

    /// <summary>The registered pairing authority.</summary>
    private readonly SasPairingAuthority authority;

    /// <summary>The native host.</summary>
    private readonly SasPairingHost host;

    /// <summary>The handle of every run seen so far, by the package's run object.</summary>
    private readonly Dictionary<SasPairingRun, NativeRunHandle> handlesByRun = new(ReferenceEqualityComparer.Instance);

    /// <summary>The package's run object behind every handle.</summary>
    private readonly Dictionary<NativeRunHandle, SasPairingRun> runsByHandle = [];

    /// <summary>The ceremony identity object of each run's latest presentation, which only the package can construct.</summary>
    private readonly Dictionary<NativeRunHandle, SasPairingCeremonyIdentity> presentedIdentities = [];

    /// <summary>Wraps an opened runtime, authority, and host, taking ownership of all three.</summary>
    /// <param name="runtime">The loaded runtime.</param>
    /// <param name="authority">The registered authority.</param>
    /// <param name="host">The native host.</param>
    private SasPairingNativeSession(SasPairingRuntime runtime, SasPairingAuthority authority, SasPairingHost host)
    {
        this.runtime = runtime;
        this.authority = authority;
        this.host = host;
    }

    /// <summary>Loads the native library, registers the authority scope, and creates a host; releases anything opened on failure.</summary>
    /// <param name="nativeLibraryPath">The absolute native library path.</param>
    /// <param name="authorityScope">The pairing authority scope.</param>
    /// <returns>The opened session.</returns>
    /// <exception cref="PairingCeremonyNativeException">Loading, registration, or host creation failed.</exception>
    public static SasPairingNativeSession Open(string nativeLibraryPath, ReadOnlySpan<byte> authorityScope)
    {
        SasPairingRuntime? runtime = null;
        SasPairingAuthority? authority = null;
        try
        {
            runtime = Guard("SasPairingRuntime.Create", () => SasPairingRuntime.Create(nativeLibraryPath));
            byte[] scope = authorityScope.ToArray();
            authority = Guard("SasPairingRuntime.RegisterAuthority", () => runtime.RegisterAuthority(scope));
            SasPairingHost host = Guard("SasPairingAuthority.CreateHost", authority.CreateHost);
            return new SasPairingNativeSession(runtime, authority, host);
        }
        catch (PairingCeremonyNativeException)
        {
            ReleaseQuietly(authority);
            ReleaseQuietly(runtime);
            throw;
        }
    }

    /// <inheritdoc/>
    public void AttachListener(Socket boundListener, CeremonyBootstrapFields localBootstrap)
    {
        var local = new SasPairingBootstrap(
            localBootstrap.ApplicationIdentity, localBootstrap.KeyAlgorithm, localBootstrap.PublicKey, localBootstrap.SharedContext);
        using SasPairingWindowsListenerSocket token = Guard("SasPairingWindowsListenerSocket.FromSocket", () => SasPairingWindowsListenerSocket.FromSocket(boundListener));
        Guard("SasPairingHost.AttachWindowsListener", () => host.AttachWindowsListener(token, local));
    }

    /// <inheritdoc/>
    public NativeDriveResult Drive()
    {
        SasPairingDriveBatch batch = Guard("SasPairingHost.Drive", host.Drive);
        var batchEvents = new List<SasPairingEvent>(batch.Events);
        List<NativeCeremonyEvent> events;
        try
        {
            events = TranslateAndCleanupBatch(
                batchEvents,
                evt =>
                {
                    CeremonyResultSnapshot? result = evt.Result is null ? null : ReadOnceAndRelease(evt.Result);
                    NativeRunHandle? run = evt.Run is null ? null : HandleFor(evt.Run);
                    return new NativeCeremonyEvent(
                        Translate(evt.Kind), Translate(evt.ProtocolEvent), run, evt.Run?.IsEnded ?? false, result);
                },
                evt => evt.Result,
                evt => evt.ShouldDisposeConnection,
                evt => evt.Connection,
                evt => evt.Run?.IsEnded ?? false,
                evt => ForgetEnded(evt.Run!),
                ReleaseQuietly);
        }
        finally
        {
            ForgetEndedRuns();
        }

        NativeFailureKind? failure = batch.Failure switch
        {
            null => null,
            { ProcessRestartRequired: true } => NativeFailureKind.ProcessFatal,
            _ => NativeFailureKind.OwnerLoopFailed,
        };
        return new NativeDriveResult(events, failure);
    }

    /// <inheritdoc/>
    public void AuthorizeExposure(NativeRunHandle run) => Guard("SasPairingRun.AuthorizeExposure", () => RunFor(run).AuthorizeExposure());

    /// <inheritdoc/>
    public void ExposeKey(NativeRunHandle run) => Guard("SasPairingRun.ExposeKey", () => RunFor(run).ExposeKey());

    /// <inheritdoc/>
    public NativeSasPresentation? Presentation(NativeRunHandle run)
    {
        SasPairingSasPresentation? presentation = Guard("SasPairingRun.Presentation", () => RunFor(run).Presentation());
        if (presentation is null)
        {
            return null;
        }

        presentedIdentities[run] = presentation.CeremonyIdentity;
        return new NativeSasPresentation(presentation.CeremonyIdentity.Bytes.ToArray(), presentation.DecimalDisplay);
    }

    /// <inheritdoc/>
    public void ApproveSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity)
    {
        SasPairingCeremonyIdentity identity = PresentedIdentity(run, ceremonyIdentity);
        Guard("SasPairingRun.ApproveSas", () => RunFor(run).ApproveSas(identity));
    }

    /// <inheritdoc/>
    public void EmitBootstrapMac(NativeRunHandle run) => Guard("SasPairingRun.EmitBootstrapMac", () => RunFor(run).EmitBootstrapMac());

    /// <inheritdoc/>
    public void RejectSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity)
    {
        SasPairingCeremonyIdentity identity = PresentedIdentity(run, ceremonyIdentity);
        Guard("SasPairingRun.RejectSas", () => RunFor(run).RejectSas(identity));
    }

    /// <inheritdoc/>
    public void CancelSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity)
    {
        SasPairingCeremonyIdentity identity = PresentedIdentity(run, ceremonyIdentity);
        Guard("SasPairingRun.CancelSas", () => RunFor(run).CancelSas(identity));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ReleaseQuietly(host);
        ReleaseQuietly(authority);
        ReleaseQuietly(runtime);
    }

    /// <summary>Translates one event batch and releases every owned resource on success or failure.</summary>
    /// <typeparam name="TEvent">The event value.</typeparam>
    /// <typeparam name="TOutput">The detached translated value.</typeparam>
    /// <param name="events">The package events in their original order.</param>
    /// <param name="translate">Translates one event and releases its result on every path when present.</param>
    /// <param name="result">Gets the event's result resource, if any.</param>
    /// <param name="shouldDisposeConnection">Whether the event transfers its connection for disposal after translation.</param>
    /// <param name="connection">Gets the event's connection resource, if any.</param>
    /// <param name="runEnded">Whether the event names an ended run.</param>
    /// <param name="forgetEndedRun">Forgets an ended run after every event has been translated.</param>
    /// <param name="release">Releases a result or flagged connection.</param>
    /// <returns>The translated events, in their original order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="Exception"><paramref name="translate"/> fails; remaining resources are still released.</exception>
    internal static List<TOutput> TranslateAndCleanupBatch<TEvent, TOutput>(
        IReadOnlyList<TEvent> events,
        Func<TEvent, TOutput> translate,
        Func<TEvent, IDisposable?> result,
        Func<TEvent, bool> shouldDisposeConnection,
        Func<TEvent, IDisposable?> connection,
        Func<TEvent, bool> runEnded,
        Action<TEvent> forgetEndedRun,
        Action<IDisposable?> release)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(translate);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(shouldDisposeConnection);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(runEnded);
        ArgumentNullException.ThrowIfNull(forgetEndedRun);
        ArgumentNullException.ThrowIfNull(release);

        var translated = new List<TOutput>(events.Count);
        var resultReleased = new bool[events.Count];
        try
        {
            for (int index = 0; index < events.Count; index++)
            {
                TEvent evt = events[index];
                resultReleased[index] = result(evt) is not null;
                translated.Add(translate(evt));
            }
        }
        finally
        {
            for (int index = 0; index < events.Count; index++)
            {
                TEvent evt = events[index];
                if (!resultReleased[index] && result(evt) is { } unprocessedResult)
                {
                    release(unprocessedResult);
                }

                if (shouldDisposeConnection(evt) && connection(evt) is { } flaggedConnection)
                {
                    release(flaggedConnection);
                }
            }

            foreach (TEvent evt in events)
            {
                if (runEnded(evt))
                {
                    forgetEndedRun(evt);
                }
            }
        }

        return translated;
    }

    /// <summary>Forgets ended runs from a stable snapshot while preserving every still-live run.</summary>
    /// <typeparam name="TRun">The tracked run value.</typeparam>
    /// <param name="runs">The current run values.</param>
    /// <param name="isEnded">Whether a run has ended.</param>
    /// <param name="forget">Removes one ended run from its owning maps.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    internal static void ForgetEndedRuns<TRun>(
        IReadOnlyCollection<TRun> runs,
        Func<TRun, bool> isEnded,
        Action<TRun> forget)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(isEnded);
        ArgumentNullException.ThrowIfNull(forget);

        foreach (TRun run in runs.ToArray())
        {
            if (isEnded(run))
            {
                forget(run);
            }
        }
    }

    /// <summary>Disposes a native object, ignoring cleanup failures so every later release still runs.</summary>
    /// <param name="resource">The object to release, or <see langword="null"/>.</param>
    internal static void ReleaseQuietly(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception exception) when (exception is SasPairingNativeException or SasPairingContractException)
        {
            // Native cleanup reports a failure, but the object is disposed regardless; nothing is retried.
        }
    }

    /// <summary>Runs one package call and classifies every package failure.</summary>
    /// <typeparam name="T">The call's result type.</typeparam>
    /// <param name="operation">The operation name for diagnostics.</param>
    /// <param name="call">The package call.</param>
    /// <returns>The call's result.</returns>
    /// <exception cref="PairingCeremonyNativeException">The call failed.</exception>
    private static T Guard<T>(string operation, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (SasPairingRunEndedException exception)
        {
            throw new PairingCeremonyNativeException(NativeFailureKind.RunEnded, operation, "RunEnded", exception);
        }
        catch (SasPairingNativeException exception)
        {
            NativeFailureKind kind = exception.ProcessRestartRequired ? NativeFailureKind.ProcessFatal : exception.KnownStatus switch
            {
                SasPairingStatus.WritePending => NativeFailureKind.WritePending,
                SasPairingStatus.RunEnded or SasPairingStatus.ConnectionEnded => NativeFailureKind.RunEnded,
                SasPairingStatus.OwnershipUnavailable => NativeFailureKind.OwnershipUnavailable,
                SasPairingStatus.OwnershipUncertain or SasPairingStatus.OwnerLoopClosed => NativeFailureKind.OwnerLoopFailed,
                _ => NativeFailureKind.OperationFailed,
            };
            throw new PairingCeremonyNativeException(kind, operation, exception.KnownStatus?.ToString() ?? $"status {exception.StatusCode}", exception);
        }
        catch (SasPairingContractException exception)
        {
            throw new PairingCeremonyNativeException(NativeFailureKind.ContractViolation, operation, "ContractViolation", exception);
        }
        catch (SasPairingInitializationException exception)
        {
            NativeFailureKind kind = exception.ProcessRestartRequired ? NativeFailureKind.ProcessFatal : NativeFailureKind.OperationFailed;
            throw new PairingCeremonyNativeException(kind, operation, exception.Failure.ToString(), exception);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or PlatformNotSupportedException or ArgumentException)
        {
            throw new PairingCeremonyNativeException(NativeFailureKind.OperationFailed, operation, exception.GetType().Name, exception);
        }
    }

    /// <summary>Runs one package call without a result and classifies every package failure.</summary>
    /// <param name="operation">The operation name for diagnostics.</param>
    /// <param name="call">The package call.</param>
    /// <exception cref="PairingCeremonyNativeException">The call failed.</exception>
    private static void Guard(string operation, Action call) => Guard(operation, () =>
    {
        call();
        return true;
    });

    /// <summary>Reads a local result exactly once into detached values and releases the native result on every path.</summary>
    /// <param name="result">The result an event delivered.</param>
    /// <returns>The detached snapshot.</returns>
    /// <exception cref="PairingCeremonyNativeException">The read failed; the result is still released.</exception>
    private static CeremonyResultSnapshot ReadOnceAndRelease(SasPairingResult result)
    {
        try
        {
            SasPairingResultData data = Guard("SasPairingResult.Read", result.Read);
            return new CeremonyResultSnapshot(
                data.CeremonyIdentity.Bytes,
                data.PeerRole == SasPairingPeerRole.Initiator ? CeremonyPeerRole.Initiator : CeremonyPeerRole.Responder,
                data.ProfileVersion,
                data.RequestId,
                data.AuthenticatedPeerBootstrap,
                data.AuthenticatedSharedContext,
                data.ProfileIdentifier);
        }
        finally
        {
            ReleaseQuietly(result);
        }
    }

    /// <summary>Translates a package event kind.</summary>
    /// <param name="kind">The package value.</param>
    /// <returns>The DovahLink value.</returns>
    private static NativeCeremonyEventKind Translate(SasPairingEventKind kind) => kind switch
    {
        SasPairingEventKind.ConnectionAccepted => NativeCeremonyEventKind.ConnectionAccepted,
        SasPairingEventKind.AcceptRefused => NativeCeremonyEventKind.AcceptRefused,
        SasPairingEventKind.ListenerDisabled => NativeCeremonyEventKind.ListenerDisabled,
        SasPairingEventKind.ConnectionClosed => NativeCeremonyEventKind.ConnectionClosed,
        _ => NativeCeremonyEventKind.ConnectionStep,
    };

    /// <summary>Translates the protocol events the Responder acts on; every other value is <see cref="NativeProtocolEvent.Other"/>.</summary>
    /// <param name="protocolEvent">The package value.</param>
    /// <returns>The DovahLink value.</returns>
    private static NativeProtocolEvent Translate(SasPairingProtocolEvent protocolEvent) => protocolEvent switch
    {
        SasPairingProtocolEvent.StartAccepted => NativeProtocolEvent.StartAccepted,
        SasPairingProtocolEvent.InitiatorKey => NativeProtocolEvent.InitiatorKey,
        SasPairingProtocolEvent.BootstrapMacAuthenticated => NativeProtocolEvent.BootstrapMacAuthenticated,
        SasPairingProtocolEvent.PeerCancel => NativeProtocolEvent.PeerCancel,
        _ => NativeProtocolEvent.Other,
    };

    /// <summary>Returns the stable handle of a package run, creating it on first sight.</summary>
    /// <param name="run">The package run.</param>
    /// <returns>The handle.</returns>
    private NativeRunHandle HandleFor(SasPairingRun run)
    {
        if (!handlesByRun.TryGetValue(run, out NativeRunHandle? handle))
        {
            handle = new NativeRunHandle();
            handlesByRun.Add(run, handle);
            runsByHandle.Add(handle, run);
        }

        return handle;
    }

    /// <summary>Forgets a run after its final event batch has been translated.</summary>
    /// <param name="run">The ended package run.</param>
    private void ForgetEnded(SasPairingRun run)
    {
        if (handlesByRun.Remove(run, out NativeRunHandle? handle))
        {
            runsByHandle.Remove(handle);
            presentedIdentities.Remove(handle);
        }
    }

    /// <summary>Forgets every tracked run the package has observed ending, including runs absent from this batch.</summary>
    private void ForgetEndedRuns()
    {
        ForgetEndedRuns(runsByHandle.Values, run => run.IsEnded, ForgetEnded);
    }

    /// <summary>Resolves a handle to its package run.</summary>
    /// <param name="handle">The handle.</param>
    /// <returns>The package run.</returns>
    /// <exception cref="PairingCeremonyNativeException">The handle is not one of this session's runs.</exception>
    private SasPairingRun RunFor(NativeRunHandle handle) =>
        runsByHandle.TryGetValue(handle, out SasPairingRun? run)
            ? run
            : throw new PairingCeremonyNativeException(NativeFailureKind.OperationFailed, "RunFor", "UnknownRun");

    /// <summary>Returns the package identity of a run's latest presentation, requiring the caller's bytes to equal it.</summary>
    /// <param name="run">The run.</param>
    /// <param name="ceremonyIdentity">The identity bytes a decision named.</param>
    /// <returns>The package identity object.</returns>
    /// <exception cref="PairingCeremonyNativeException">The run has no presentation or the bytes differ.</exception>
    private SasPairingCeremonyIdentity PresentedIdentity(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) =>
        presentedIdentities.TryGetValue(run, out SasPairingCeremonyIdentity? identity) && identity.Bytes.SequenceEqual(ceremonyIdentity)
            ? identity
            : throw new PairingCeremonyNativeException(NativeFailureKind.OperationFailed, "PresentedIdentity", "CeremonyIdentityMismatch");
}
