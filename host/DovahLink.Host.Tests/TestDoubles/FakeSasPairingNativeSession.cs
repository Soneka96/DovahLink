using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A thread-safe, scriptable stand-in for the native sas-pairing session. Drives return queued
/// results, or block up to <see cref="DriveBound"/> like the real bounded drive when none is queued.
/// Every call is recorded with the thread that made it, and any step can be made to fail.
/// </summary>
internal sealed class FakeSasPairingNativeSession : ISasPairingNativeSession
{
    /// <summary>The queued drive results, consumed in order.</summary>
    private readonly BlockingCollection<NativeDriveResult> driveResults = [];

    /// <summary>Failures to throw from the next call of a step, by step name.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentQueue<NativeFailureKind>> stepFailures = new();

    /// <summary>The presentation each run reports, by run.</summary>
    private readonly ConcurrentDictionary<NativeRunHandle, NativeSasPresentation> presentations = new();

    /// <summary>Backing field of <see cref="DriveCount"/>.</summary>
    private int driveCount;

    /// <summary>Backing field of <see cref="CompletedDriveCount"/>.</summary>
    private int completedDriveCount;

    /// <summary>Backing field of <see cref="DisposeCount"/>.</summary>
    private int disposeCount;

    /// <summary>Every recorded call as <c>(name, run)</c>, in order.</summary>
    public ConcurrentQueue<(string Name, NativeRunHandle? Run)> Calls { get; } = new();

    /// <summary>The managed thread ID of every recorded call.</summary>
    public ConcurrentBag<int> CallingThreads { get; } = [];

    /// <summary>Whether any recorded call ran on a thread-pool thread.</summary>
    public bool AnyCallOnThreadPool { get; private set; }

    /// <summary>The endpoint of the socket handed to <see cref="AttachListener"/>, once attached.</summary>
    public IPEndPoint? AttachedEndpoint { get; private set; }

    /// <summary>How long a drive with nothing queued blocks, like the real bounded drive.</summary>
    public TimeSpan DriveBound { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>When set, the next drive throws this unclassified exception instead of a native failure.</summary>
    public Exception? UnexpectedDriveException { get; set; }

    /// <summary>When set, the next attach throws this classification.</summary>
    public NativeFailureKind? AttachFailure { get; set; }

    /// <summary>The number of drives started.</summary>
    public int DriveCount => Volatile.Read(ref driveCount);

    /// <summary>The number of drives that have returned.</summary>
    public int CompletedDriveCount => Volatile.Read(ref completedDriveCount);

    /// <summary>The number of times the session was disposed.</summary>
    public int DisposeCount => Volatile.Read(ref disposeCount);

    /// <summary>The number of drives that had already returned when the session was first disposed.</summary>
    public int DrivesCompletedAtDispose { get; private set; } = -1;

    /// <summary>Queues the result of a future drive.</summary>
    /// <param name="events">The events the drive returns.</param>
    /// <param name="failure">The owner-loop failure the drive reports, if any.</param>
    public void QueueDrive(IEnumerable<NativeCeremonyEvent> events, NativeFailureKind? failure = null) =>
        driveResults.Add(new NativeDriveResult([.. events], failure));

    /// <summary>Makes the next call of one step throw.</summary>
    /// <param name="step">The step name, for example <c>ApproveSas</c>.</param>
    /// <param name="kind">The classification to throw.</param>
    public void FailNext(string step, NativeFailureKind kind) => stepFailures.GetOrAdd(step, _ => new()).Enqueue(kind);

    /// <summary>Sets the presentation a run reports once exposed.</summary>
    /// <param name="run">The run.</param>
    /// <param name="presentation">The presentation.</param>
    public void SetPresentation(NativeRunHandle run, NativeSasPresentation presentation) => presentations[run] = presentation;

    /// <summary>Counts the recorded calls of one step.</summary>
    /// <param name="name">The step name.</param>
    /// <returns>The number of calls.</returns>
    public int CallCount(string name) => Calls.Count(call => call.Name == name);

    /// <inheritdoc/>
    public void AttachListener(Socket boundListener, CeremonyBootstrapFields localBootstrap)
    {
        Record("AttachListener", null);
        if (AttachFailure is NativeFailureKind failure)
        {
            throw new PairingCeremonyNativeException(failure, "AttachListener", failure.ToString());
        }

        AttachedEndpoint = (IPEndPoint?)boundListener.LocalEndPoint;
    }

    /// <inheritdoc/>
    public NativeDriveResult Drive()
    {
        Interlocked.Increment(ref driveCount);
        Record("Drive", null);
        try
        {
            if (UnexpectedDriveException is Exception unexpected)
            {
                UnexpectedDriveException = null;
                throw unexpected;
            }

            ThrowIfFailing("Drive");
            return driveResults.TryTake(out NativeDriveResult? result, DriveBound) ? result : new NativeDriveResult([], null);
        }
        finally
        {
            Interlocked.Increment(ref completedDriveCount);
        }
    }

    /// <inheritdoc/>
    public void AuthorizeExposure(NativeRunHandle run) => Step("AuthorizeExposure", run);

    /// <inheritdoc/>
    public void ExposeKey(NativeRunHandle run) => Step("ExposeKey", run);

    /// <inheritdoc/>
    public NativeSasPresentation? Presentation(NativeRunHandle run)
    {
        Step("Presentation", run);
        return presentations.TryGetValue(run, out NativeSasPresentation? presentation) ? presentation : null;
    }

    /// <inheritdoc/>
    public void ApproveSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => Step("ApproveSas", run);

    /// <inheritdoc/>
    public void EmitBootstrapMac(NativeRunHandle run) => Step("EmitBootstrapMac", run);

    /// <inheritdoc/>
    public void RejectSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => Step("RejectSas", run);

    /// <inheritdoc/>
    public void CancelSas(NativeRunHandle run, ReadOnlySpan<byte> ceremonyIdentity) => Step("CancelSas", run);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Increment(ref disposeCount) == 1)
        {
            DrivesCompletedAtDispose = CompletedDriveCount;
        }

        Record("Dispose", null);
    }

    /// <summary>Records a step call and throws a configured failure.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="run">The run.</param>
    private void Step(string name, NativeRunHandle run)
    {
        Record(name, run);
        ThrowIfFailing(name);
    }

    /// <summary>Throws the next configured failure of a step, if any.</summary>
    /// <param name="name">The step name.</param>
    private void ThrowIfFailing(string name)
    {
        if (stepFailures.TryGetValue(name, out ConcurrentQueue<NativeFailureKind>? failures) && failures.TryDequeue(out NativeFailureKind kind))
        {
            throw new PairingCeremonyNativeException(kind, name, kind.ToString());
        }
    }

    /// <summary>Records one call and its thread.</summary>
    /// <param name="name">The call name.</param>
    /// <param name="run">The run, if any.</param>
    private void Record(string name, NativeRunHandle? run)
    {
        Calls.Enqueue((name, run));
        CallingThreads.Add(Environment.CurrentManagedThreadId);
        AnyCallOnThreadPool |= Thread.CurrentThread.IsThreadPoolThread;
    }
}
