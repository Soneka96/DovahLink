using System.Diagnostics;
using System.Net;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.PairingCeremony.Native;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Tests the dormant ceremony host's owner thread, explicit decision boundary, fail-closed latch, and
/// shutdown against a scriptable fake native session.
/// </summary>
public sealed class PairingCeremonyHostTests : IDisposable
{
    /// <summary>The fake native session the host drives.</summary>
    private readonly FakeSasPairingNativeSession session = new();

    /// <summary>The factory handing out <see cref="session"/>.</summary>
    private readonly FakeSasPairingNativeSessionFactory factory;

    /// <summary>The observer recording the host's notifications.</summary>
    private readonly RecordingPairingCeremonyObserver observer = new();

    /// <summary>The host under test.</summary>
    private readonly PairingCeremonyHost host;

    /// <summary>Creates an unstarted host over the fakes.</summary>
    public PairingCeremonyHostTests()
    {
        factory = new FakeSasPairingNativeSessionFactory(session);
        host = new PairingCeremonyHost(Fixtures.BuildPairingCeremonyHostOptions(), observer, factory);
    }

    /// <summary>Verifies start opens the native host and binds a loopback-only listener, all on one owner thread.</summary>
    [Fact]
    public void Start_OpensNativeHostAndLoopbackListenerOnOneOwnerThread()
    {
        Assert.True(host.Start());

        Assert.Equal(PairingCeremonyHostState.Running, host.State);
        IPEndPoint endpoint = host.ListenerEndpoint!;
        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.NotEqual(0, endpoint.Port);
        Assert.Equal(endpoint, session.AttachedEndpoint);
        Assert.Equal(Fixtures.BuildPairingCeremonyHostOptions().AuthorityScope.ToArray(), factory.OpenedScope);
        Eventually(() => session.DriveCount > 0);
        Assert.NotEqual(Environment.CurrentManagedThreadId, factory.OpeningThread);
        Assert.Equal([factory.OpeningThread], session.CallingThreads.Distinct());
        Assert.False(session.AnyCallOnThreadPool);
    }

    /// <summary>Verifies a host starts at most once, including after it stopped.</summary>
    [Fact]
    public void Start_Twice_Throws()
    {
        host.Start();

        Assert.Throws<InvalidOperationException>(() => host.Start());
        host.Stop();
        Assert.Throws<InvalidOperationException>(() => host.Start());
        Assert.Equal(1, factory.OpenCount);
    }

    /// <summary>Verifies stop releases the native session once, ends the owner thread, and is idempotent.</summary>
    [Fact]
    public void Stop_ReleasesSessionOnceAndIsIdempotent()
    {
        host.Start();

        host.Stop();
        host.Stop();

        Assert.Equal(PairingCeremonyHostState.Stopped, host.State);
        Assert.Null(host.ListenerEndpoint);
        Assert.Null(host.Failure);
        Assert.Equal(1, session.DisposeCount);
        Assert.False(host.TryAuthorizeExposure(new CeremonyAttemptId(1)));
    }

    /// <summary>Verifies stopping an unstarted host never opens anything.</summary>
    [Fact]
    public void Stop_BeforeStart_OpensNothing()
    {
        host.Stop();

        Assert.Equal(PairingCeremonyHostState.Stopped, host.State);
        Assert.Equal(0, factory.OpenCount);
        Assert.Throws<InvalidOperationException>(() => host.Start());
    }

    /// <summary>Verifies stop while a bounded drive is waiting lets that drive finish, then releases the session, within the bound.</summary>
    [Fact]
    public void Stop_WhileDriveWaits_FinishesTheDriveThenReleasesWithinBound()
    {
        session.DriveBound = TimeSpan.FromMilliseconds(400);
        host.Start();
        Eventually(() => session.DriveCount > session.CompletedDriveCount);

        var stopwatch = Stopwatch.StartNew();
        host.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Stop took {stopwatch.Elapsed}.");
        Assert.Equal(session.DriveCount, session.DrivesCompletedAtDispose);
        Assert.Equal(1, session.DisposeCount);
    }

    /// <summary>
    /// Verifies the honest Responder path needs an explicit exposure authorization and an explicit MATCH
    /// naming the exact ceremony identity, and that the local result is forwarded detached.
    /// </summary>
    [Fact]
    public void ResponderFlow_RequiresExplicitAuthorizationAndExactMatchDecision()
    {
        var run = new NativeRunHandle();
        byte[] identity = Enumerable.Repeat((byte)0x5a, 32).ToArray();
        session.SetPresentation(run, new NativeSasPresentation(identity, "1234 5678 9012"));
        host.Start();

        session.QueueDrive([StartAccepted(run), InitiatorKey(run)]);
        Eventually(() => observer.ExposureRequests.Count == 1);
        Thread.Sleep(100);
        Assert.Equal(0, session.CallCount("AuthorizeExposure"));
        Assert.Equal(0, session.CallCount("ExposeKey"));

        Assert.True(host.TryAuthorizeExposure(observer.ExposureRequests.Single()));
        Eventually(() => observer.SasRequests.Count == 1);
        SasComparisonRequest request = observer.SasRequests.Single();
        Assert.Equal("1234 5678 9012", request.DecimalDisplay);
        Assert.True(request.CeremonyIdentity.SequenceEqual(identity));
        Assert.Equal(1, session.CallCount("AuthorizeExposure"));
        Assert.Equal(1, session.CallCount("ExposeKey"));
        Thread.Sleep(100);
        Assert.Equal(0, session.CallCount("ApproveSas"));

        Assert.True(host.TrySubmitSasDecision(identity, SasComparisonDecision.Match));
        Eventually(() => session.CallCount("EmitBootstrapMac") == 1);
        Assert.Equal(1, session.CallCount("ApproveSas"));

        CeremonyResultSnapshot result = Fixtures.BuildCeremonyResultSnapshot();
        session.QueueDrive([ResultEvent(run, result)]);
        Eventually(() => observer.Completions.Count == 1);
        Assert.Equal(request.Attempt, observer.Completions.Single().Attempt);
        Assert.Same(result, observer.Completions.Single().Result);
        Assert.Empty(observer.EndedAttempts);
        Assert.DoesNotContain(observer.NotifyingThreads, thread => thread == Environment.CurrentManagedThreadId);
    }

    /// <summary>Verifies a SAS decision naming any other ceremony identity changes nothing.</summary>
    [Fact]
    public void SasDecision_ForOtherCeremonyIdentity_ChangesNothing()
    {
        (NativeRunHandle _, byte[] identity) = BringToSasDecision();
        byte[] other = (byte[])identity.Clone();
        other[^1] ^= 0x01;

        foreach (SasComparisonDecision decision in Enum.GetValues<SasComparisonDecision>())
        {
            Assert.True(host.TrySubmitSasDecision(other, decision));
        }

        Thread.Sleep(150);
        Assert.Equal(0, session.CallCount("ApproveSas"));
        Assert.Equal(0, session.CallCount("RejectSas"));
        Assert.Equal(0, session.CallCount("CancelSas"));
        Assert.Empty(observer.EndedAttempts);
    }

    /// <summary>Verifies MISMATCH rejects and CANCEL cancels the exact ceremony, ending the attempt without approving it.</summary>
    /// <param name="mismatch">Whether to decide MISMATCH instead of CANCEL.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SasDecision_MismatchOrCancel_EndsAttemptWithoutApproval(bool mismatch)
    {
        (NativeRunHandle _, byte[] identity) = BringToSasDecision();

        host.TrySubmitSasDecision(identity, mismatch ? SasComparisonDecision.Mismatch : SasComparisonDecision.Cancel);

        Eventually(() => observer.EndedAttempts.Count == 1);
        Assert.Equal(1, session.CallCount(mismatch ? "RejectSas" : "CancelSas"));
        Assert.Equal(0, session.CallCount("ApproveSas"));
        Assert.Equal(0, session.CallCount("EmitBootstrapMac"));
        host.TrySubmitSasDecision(identity, SasComparisonDecision.Match);
        Thread.Sleep(100);
        Assert.Equal(0, session.CallCount("ApproveSas"));
    }

    /// <summary>
    /// Verifies an exposure authorization given before the host asked for it, or naming another
    /// attempt, is refused and exposes nothing, even once the peer's key arrives.
    /// </summary>
    [Fact]
    public void ExposureAuthorization_ForWrongAttemptOrTooEarly_IsRefusedAndExposesNothing()
    {
        var run = new NativeRunHandle();
        host.Start();
        session.QueueDrive([StartAccepted(run)]);
        Eventually(() => session.DriveCount > 2);

        Assert.False(host.TryAuthorizeExposure(new CeremonyAttemptId(1)));
        session.QueueDrive([InitiatorKey(run)]);
        Eventually(() => observer.ExposureRequests.Count == 1);
        Assert.Equal(new CeremonyAttemptId(1), observer.ExposureRequests.Single());
        Assert.False(host.TryAuthorizeExposure(new CeremonyAttemptId(99)));
        Thread.Sleep(150);

        Assert.Equal(0, session.CallCount("AuthorizeExposure"));
        Assert.Equal(0, session.CallCount("ExposeKey"));
    }

    /// <summary>Verifies a retained outbound frame postpones a step to a later iteration instead of ending the attempt.</summary>
    [Fact]
    public void WritePending_PostponesTheStepUntilItRuns()
    {
        (NativeRunHandle _, byte[] identity) = BringToSasDecision();
        session.FailNext("EmitBootstrapMac", NativeFailureKind.WritePending);

        host.TrySubmitSasDecision(identity, SasComparisonDecision.Match);

        Eventually(() => session.CallCount("EmitBootstrapMac") == 2);
        Assert.Empty(observer.EndedAttempts);
        Assert.Equal(PairingCeremonyHostState.Running, host.State);
    }

    /// <summary>Verifies an ended run, or a failed step, ends only that attempt while the host keeps running.</summary>
    /// <param name="endedByEvent">Whether the run ends through a drive event instead of a failed step.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttemptEndOrStepFailure_EndsOnlyThatAttempt(bool endedByEvent)
    {
        var run = new NativeRunHandle();
        host.Start();
        session.QueueDrive([StartAccepted(run), InitiatorKey(run)]);
        Eventually(() => observer.ExposureRequests.Count == 1);

        if (endedByEvent)
        {
            session.QueueDrive([new NativeCeremonyEvent(NativeCeremonyEventKind.ConnectionClosed, NativeProtocolEvent.Other, run, true, null)]);
        }
        else
        {
            session.FailNext("AuthorizeExposure", NativeFailureKind.OperationFailed);
            host.TryAuthorizeExposure(observer.ExposureRequests.Single());
        }

        Eventually(() => observer.EndedAttempts.Count == 1);
        Assert.Equal(observer.ExposureRequests.Single(), observer.EndedAttempts.Single());
        Assert.Equal(PairingCeremonyHostState.Running, host.State);
        Assert.Empty(observer.Failures);
    }

    /// <summary>
    /// Verifies FATAL, a contract violation, or a failed owner loop latches the host failed: no more
    /// drives, decisions refused, the session still released, and nothing reopened or reattached.
    /// </summary>
    /// <param name="driveFailureName">The name of the native failure the drive reports.</param>
    /// <param name="expected">The resulting host failure.</param>
    /// <param name="restartRequired">Whether a process restart is required afterward.</param>
    [Theory]
    [InlineData("ProcessFatal", PairingCeremonyFailure.ProcessFatal, true)]
    [InlineData("ContractViolation", PairingCeremonyFailure.ContractViolation, true)]
    [InlineData("OwnerLoopFailed", PairingCeremonyFailure.OwnerLoopFailed, false)]
    public void DriveFailure_LatchesFailedWithoutRecreatingAnything(string driveFailureName, PairingCeremonyFailure expected, bool restartRequired)
    {
        host.Start();

        session.QueueDrive([], Enum.Parse<NativeFailureKind>(driveFailureName));

        Eventually(() => host.State == PairingCeremonyHostState.Failed);
        Eventually(() => session.DisposeCount == 1);
        int drives = session.DriveCount;
        Thread.Sleep(150);
        Assert.Equal(drives, session.DriveCount);
        Assert.Equal(expected, host.Failure);
        Assert.Equal(restartRequired, host.ProcessRestartRequired);
        Assert.Equal((expected, restartRequired), observer.Failures.Single());
        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, session.CallCount("AttachListener"));
        Assert.False(host.TryAuthorizeExposure(new CeremonyAttemptId(1)));
        Assert.False(host.TrySubmitSasDecision(new byte[32], SasComparisonDecision.Match));
        Assert.Throws<InvalidOperationException>(() => host.Start());
        host.Stop();
        Assert.Equal(PairingCeremonyHostState.Failed, host.State);
    }

    /// <summary>Verifies FATAL raised by a ceremony step, not just by a drive, latches the whole host.</summary>
    [Fact]
    public void FatalFromStep_LatchesHostAndRequiresProcessRestart()
    {
        (NativeRunHandle _, byte[] identity) = BringToSasDecision();
        session.FailNext("ApproveSas", NativeFailureKind.ProcessFatal);

        host.TrySubmitSasDecision(identity, SasComparisonDecision.Match);

        Eventually(() => host.State == PairingCeremonyHostState.Failed);
        Assert.Equal(PairingCeremonyFailure.ProcessFatal, host.Failure);
        Assert.True(host.ProcessRestartRequired);
        Eventually(() => session.DisposeCount == 1);
        Assert.Equal(0, session.CallCount("EmitBootstrapMac"));
        Assert.Equal(1, factory.OpenCount);
    }

    /// <summary>Verifies a failed open or attach fails start closed, releasing whatever was opened.</summary>
    /// <param name="failOpen">Whether opening fails instead of attaching.</param>
    /// <param name="kindName">The name of the native failure.</param>
    /// <param name="expected">The resulting host failure.</param>
    [Theory]
    [InlineData(true, "OwnershipUnavailable", PairingCeremonyFailure.AuthorityUnavailable)]
    [InlineData(true, "ProcessFatal", PairingCeremonyFailure.ProcessFatal)]
    [InlineData(true, "OperationFailed", PairingCeremonyFailure.StartFailed)]
    [InlineData(false, "OperationFailed", PairingCeremonyFailure.StartFailed)]
    public void StartFailure_FailsClosedAndReleasesWhatWasOpened(bool failOpen, string kindName, PairingCeremonyFailure expected)
    {
        NativeFailureKind kind = Enum.Parse<NativeFailureKind>(kindName);
        if (failOpen)
        {
            factory.OpenFailure = kind;
        }
        else
        {
            session.AttachFailure = kind;
        }

        Assert.False(host.Start());

        Assert.Equal(PairingCeremonyHostState.Failed, host.State);
        Assert.Equal(expected, host.Failure);
        Assert.Null(host.ListenerEndpoint);
        Assert.Equal(failOpen ? 0 : 1, session.DisposeCount);
        Assert.Equal(0, session.DriveCount);
        Assert.Equal((expected, kind == NativeFailureKind.ProcessFatal), observer.Failures.Single());
    }

    /// <summary>Verifies an observer that throws never stops the owner thread or the ceremony.</summary>
    [Fact]
    public void ThrowingObserver_DoesNotStopTheCeremony()
    {
        observer.ThrowFromEveryNotification = true;

        (NativeRunHandle run, byte[] identity) = BringToSasDecision();
        host.TrySubmitSasDecision(identity, SasComparisonDecision.Match);
        session.QueueDrive([ResultEvent(run, Fixtures.BuildCeremonyResultSnapshot())]);

        Eventually(() => observer.Completions.Count == 1);
        Assert.Equal(PairingCeremonyHostState.Running, host.State);
    }

    /// <summary>Verifies decisions are refused when the host is not running and once its bounded queue is full.</summary>
    [Fact]
    public void Decisions_AreRefusedWhenNotRunningOrQueueFull()
    {
        Assert.False(host.TrySubmitSasDecision(new byte[32], SasComparisonDecision.Match));
        session.DriveBound = TimeSpan.FromSeconds(2);
        host.Start();
        Eventually(() => session.DriveCount > session.CompletedDriveCount);

        int accepted = 0;
        while (accepted < 100 && host.TrySubmitSasDecision(new byte[32], SasComparisonDecision.Cancel))
        {
            accepted++;
        }

        Assert.Equal(16, accepted);
        Assert.False(host.TrySubmitSasDecision(new byte[32], (SasComparisonDecision)42));
    }

    /// <summary>Verifies a SAS presentation that is not ready right after exposure is requested once it appears, and only then.</summary>
    [Fact]
    public void LatePresentation_IsRequestedOnceItAppears()
    {
        var run = new NativeRunHandle();
        host.Start();
        session.QueueDrive([StartAccepted(run), InitiatorKey(run)]);
        Eventually(() => observer.ExposureRequests.Count == 1);
        host.TryAuthorizeExposure(observer.ExposureRequests.Single());
        Eventually(() => session.CallCount("Presentation") >= 3);
        Assert.Empty(observer.SasRequests);

        session.SetPresentation(run, new NativeSasPresentation(Enumerable.Repeat((byte)0x11, 32).ToArray(), "0000 1111 2222"));

        Eventually(() => observer.SasRequests.Count == 1);
        Assert.Equal("0000 1111 2222", observer.SasRequests.Single().DecimalDisplay);
        Assert.Equal(1, session.CallCount("ExposeKey"));
    }

    /// <summary>Verifies disposing a running host stops it and releases the session once, and repeated disposal or stop is harmless.</summary>
    [Fact]
    public void Dispose_WhileRunning_StopsAndReleasesOnceAndIsIdempotent()
    {
        host.Start();
        Eventually(() => session.DriveCount > 0);

        host.Dispose();
        host.Dispose();
        host.Stop();

        Assert.Equal(PairingCeremonyHostState.Stopped, host.State);
        Assert.Equal(1, session.DisposeCount);
        Assert.False(host.TrySubmitSasDecision(new byte[32], SasComparisonDecision.Match));
    }

    /// <summary>Verifies an unexpected fault on the owner thread fails the host closed instead of escaping the thread.</summary>
    [Fact]
    public void UnexpectedOwnerFault_FailsClosedWithoutEscaping()
    {
        host.Start();

        session.UnexpectedDriveException = new InvalidOperationException("unexpected");

        Eventually(() => host.State == PairingCeremonyHostState.Failed);
        Assert.Equal(PairingCeremonyFailure.OwnerLoopFailed, host.Failure);
        Assert.False(host.ProcessRestartRequired);
        Eventually(() => session.DisposeCount == 1);
    }

    /// <summary>Verifies a local result whose event no longer names its run is matched to its attempt by ceremony identity.</summary>
    [Fact]
    public void ResultWithoutRun_IsMatchedToItsAttemptByCeremonyIdentity()
    {
        (NativeRunHandle _, byte[] identity) = BringToSasDecision();
        host.TrySubmitSasDecision(identity, SasComparisonDecision.Match);
        Eventually(() => session.CallCount("EmitBootstrapMac") == 1);

        session.QueueDrive([new NativeCeremonyEvent(NativeCeremonyEventKind.ConnectionStep, NativeProtocolEvent.Other, null, false, Fixtures.BuildCeremonyResultSnapshot(0x5a))]);

        Eventually(() => observer.Completions.Count == 1);
        Assert.Equal(observer.SasRequests.Single().Attempt, observer.Completions.Single().Attempt);
    }

    /// <summary>Verifies a local result whose run was never tracked is still reported, with no attempt.</summary>
    [Fact]
    public void ResultForUntrackedRun_IsReportedWithoutAttempt()
    {
        host.Start();

        session.QueueDrive([ResultEvent(new NativeRunHandle(), Fixtures.BuildCeremonyResultSnapshot())]);

        Eventually(() => observer.Completions.Count == 1);
        Assert.Null(observer.Completions.Single().Attempt);
    }

    /// <summary>Verifies drives that return nothing at once are paced, so the owner never busy-spins.</summary>
    [Fact]
    public void ImmediateEmptyDrives_ArePacedByTheIdlePause()
    {
        session.DriveBound = TimeSpan.Zero;
        host.Start();

        Thread.Sleep(300);

        Assert.InRange(session.DriveCount, 1, 60);
    }

    /// <summary>Verifies the options reject a relative native path, an empty scope, and empty required Bootstrap fields.</summary>
    [Fact]
    public void Options_RejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => Fixtures.BuildPairingCeremonyHostOptions(nativeLibraryPath: "sas_pairing_core.dll"));
        Assert.Throws<ArgumentException>(() => Fixtures.BuildPairingCeremonyHostOptions(authorityScope: []));
        Assert.Throws<ArgumentException>(() => new CeremonyBootstrapFields([], [1], [1], []));
        Assert.Throws<ArgumentException>(() => new CeremonyBootstrapFields([1], [], [1], []));
        Assert.Throws<ArgumentException>(() => new CeremonyBootstrapFields([1], [1], [], []));
        Assert.Equal(0, new CeremonyBootstrapFields([1], [1], [1], []).SharedContext.Length);
    }

    /// <inheritdoc/>
    public void Dispose() => host.Dispose();

    /// <summary>Waits until a condition holds, failing the test after five seconds.</summary>
    /// <param name="condition">The condition.</param>
    private static void Eventually(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), "The condition was not met within five seconds.");
            Thread.Sleep(5);
        }
    }

    /// <summary>Builds the event that starts a Responder run.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The event.</returns>
    private static NativeCeremonyEvent StartAccepted(NativeRunHandle run) =>
        new(NativeCeremonyEventKind.ConnectionStep, NativeProtocolEvent.StartAccepted, run, false, null);

    /// <summary>Builds the event of the Initiator's key arriving.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The event.</returns>
    private static NativeCeremonyEvent InitiatorKey(NativeRunHandle run) =>
        new(NativeCeremonyEventKind.ConnectionStep, NativeProtocolEvent.InitiatorKey, run, false, null);

    /// <summary>Builds the event delivering a local result.</summary>
    /// <param name="run">The run.</param>
    /// <param name="result">The detached result.</param>
    /// <returns>The event.</returns>
    private static NativeCeremonyEvent ResultEvent(NativeRunHandle run, CeremonyResultSnapshot result) =>
        new(NativeCeremonyEventKind.ConnectionStep, NativeProtocolEvent.Other, run, true, result);

    /// <summary>Starts the host and drives one attempt to its SAS decision.</summary>
    /// <returns>The run and its presented ceremony identity.</returns>
    private (NativeRunHandle Run, byte[] Identity) BringToSasDecision()
    {
        var run = new NativeRunHandle();
        byte[] identity = Enumerable.Repeat((byte)0x5a, 32).ToArray();
        session.SetPresentation(run, new NativeSasPresentation(identity, "1234 5678 9012"));
        host.Start();
        session.QueueDrive([StartAccepted(run), InitiatorKey(run)]);
        Eventually(() => observer.ExposureRequests.Count == 1);
        host.TryAuthorizeExposure(observer.ExposureRequests.Single());
        Eventually(() => observer.SasRequests.Count == 1);
        return (run, identity);
    }
}
