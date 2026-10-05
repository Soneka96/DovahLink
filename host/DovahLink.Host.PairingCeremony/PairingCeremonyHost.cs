using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using DovahLink.Host.PairingCeremony.Native;

namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// The one owner of this Host's sas-pairing Responder: its native runtime, authority, host, loopback
/// listener, runs, and local results. All native work happens on one dedicated owner thread, so a
/// bounded blocking drive never stalls any other Host work. Nothing is exposed or approved without an
/// explicit local decision, and nothing here establishes trust.
/// </summary>
public interface IPairingCeremonyHost : IDisposable
{
    /// <summary>The current lifecycle state.</summary>
    PairingCeremonyHostState State { get; }

    /// <summary>Why the host failed closed, or <see langword="null"/> while it has not.</summary>
    PairingCeremonyFailure? Failure { get; }

    /// <summary>Whether new pairing work now needs an OS process restart.</summary>
    bool ProcessRestartRequired { get; }

    /// <summary>The loopback endpoint the listener is bound to while running: routing information only, never identity.</summary>
    IPEndPoint? ListenerEndpoint { get; }

    /// <summary>
    /// Starts the owner thread, which opens the native host and attaches a new <c>127.0.0.1</c> listener.
    /// A host starts at most once and is never restarted.
    /// </summary>
    /// <returns>Whether the host is now running; otherwise see <see cref="Failure"/>.</returns>
    /// <exception cref="InvalidOperationException">The host was already started or stopped.</exception>
    bool Start();

    /// <summary>
    /// Queues fresh, explicit local authorization for one attempt to expose this Host's key. Only an
    /// attempt the host is currently asking about (see
    /// <see cref="IPairingCeremonyObserver.OnExposureAuthorizationRequested"/>) can be authorized; an
    /// authorization given before that request is refused, never held for later.
    /// </summary>
    /// <param name="attempt">The attempt the authorization names.</param>
    /// <returns>Whether the authorization was queued; it is refused when the attempt is not awaiting it, the host is not running, or its queue is full.</returns>
    bool TryAuthorizeExposure(CeremonyAttemptId attempt);

    /// <summary>
    /// Queues an explicit SAS decision for the presented ceremony whose identity is exactly
    /// <paramref name="ceremonyIdentity"/>; a decision naming any other identity changes nothing.
    /// </summary>
    /// <param name="ceremonyIdentity">The exact 32-byte ceremony identity of the presented SAS.</param>
    /// <param name="decision">The human decision.</param>
    /// <returns>Whether the decision was queued; it is refused when the host is not running or its queue is full.</returns>
    bool TrySubmitSasDecision(ReadOnlySpan<byte> ceremonyIdentity, SasComparisonDecision decision);

    /// <summary>
    /// Stops the owner thread and releases every native resource. The current drive, which is bounded,
    /// finishes first. Idempotent.
    /// </summary>
    /// <exception cref="TimeoutException">The owner thread did not end within its bound.</exception>
    void Stop();
}

/// <inheritdoc cref="IPairingCeremonyHost"/>
public sealed class PairingCeremonyHost : IPairingCeremonyHost
{
    /// <summary>The detached local configuration.</summary>
    private readonly PairingCeremonyHostOptions options;

    /// <summary>Receives requests and outcomes on the owner thread.</summary>
    private readonly IPairingCeremonyObserver observer;

    /// <summary>Opens the native session on the owner thread.</summary>
    private readonly ISasPairingNativeSessionFactory sessionFactory;

    /// <summary>Guards the lifecycle fields below.</summary>
    private readonly Lock gate = new();

    /// <summary>The bounded queue of explicit local decisions, drained only by the owner thread.</summary>
    private readonly Channel<CeremonyCommand> commands =
        Channel.CreateBounded<CeremonyCommand>(new BoundedChannelOptions(Constants.DecisionQueueCapacity) { SingleReader = true });

    /// <summary>Wakes an idle owner thread for a new decision or a stop request.</summary>
    private readonly AutoResetEvent wake = new(false);

    /// <summary>Set once the owner thread is running or has failed to start.</summary>
    private readonly ManualResetEventSlim startupFinished = new(false);

    /// <summary>The attempts whose exposure authorization is currently requested; written by the owner thread, read by callers.</summary>
    private readonly ConcurrentDictionary<CeremonyAttemptId, byte> exposureRequested = new();

    /// <summary>The attempts the owner thread follows, by native run; owner thread only.</summary>
    private readonly Dictionary<NativeRunHandle, CeremonyAttempt> attempts = new(ReferenceEqualityComparer.Instance);

    /// <summary>The last attempt number handed out; owner thread only.</summary>
    private long lastAttempt;

    /// <summary>Whether a stop was requested.</summary>
    private volatile bool stopRequested;

    /// <summary>The owner thread, once started.</summary>
    private Thread? ownerThread;

    /// <summary>The lifecycle state; guarded by <see cref="gate"/>.</summary>
    private PairingCeremonyHostState state = PairingCeremonyHostState.NotStarted;

    /// <summary>Why the host failed, if it did; guarded by <see cref="gate"/>.</summary>
    private PairingCeremonyFailure? failure;

    /// <summary>The bound loopback endpoint while running; guarded by <see cref="gate"/>.</summary>
    private IPEndPoint? listenerEndpoint;

    /// <summary>Whether the host was disposed; guarded by <see cref="gate"/>.</summary>
    private bool disposed;

    /// <summary>Creates a host over an explicit native-session factory.</summary>
    /// <param name="options">The detached local configuration.</param>
    /// <param name="observer">Receives requests and outcomes on the owner thread.</param>
    /// <param name="sessionFactory">Opens the native session.</param>
    internal PairingCeremonyHost(PairingCeremonyHostOptions options, IPairingCeremonyObserver observer, ISasPairingNativeSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.options = options;
        this.observer = observer;
        this.sessionFactory = sessionFactory;
    }

    /// <inheritdoc/>
    public PairingCeremonyHostState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <inheritdoc/>
    public PairingCeremonyFailure? Failure
    {
        get
        {
            lock (gate)
            {
                return failure;
            }
        }
    }

    /// <inheritdoc/>
    public bool ProcessRestartRequired => Failure is PairingCeremonyFailure.ProcessFatal or PairingCeremonyFailure.ContractViolation;

    /// <inheritdoc/>
    public IPEndPoint? ListenerEndpoint
    {
        get
        {
            lock (gate)
            {
                return state == PairingCeremonyHostState.Running ? listenerEndpoint : null;
            }
        }
    }

    /// <inheritdoc/>
    public bool Start()
    {
        lock (gate)
        {
            if (state != PairingCeremonyHostState.NotStarted || ownerThread is not null)
            {
                throw new InvalidOperationException("A pairing ceremony host starts at most once.");
            }

            ownerThread = new Thread(RunOwner) { IsBackground = true, Name = "DovahLink sas-pairing owner" };
        }

        ownerThread.Start();
        if (!startupFinished.Wait(Constants.StartTimeout))
        {
            Stop();
        }

        if (State == PairingCeremonyHostState.Running)
        {
            return true;
        }

        // A failed start returns only after the owner thread released everything it opened.
        ownerThread.Join(Constants.StopTimeout);
        return false;
    }

    /// <inheritdoc/>
    public bool TryAuthorizeExposure(CeremonyAttemptId attempt) =>
        exposureRequested.ContainsKey(attempt) && TryQueue(CeremonyCommand.AuthorizeExposure(attempt));

    /// <inheritdoc/>
    public bool TrySubmitSasDecision(ReadOnlySpan<byte> ceremonyIdentity, SasComparisonDecision decision) =>
        Enum.IsDefined(decision) && TryQueue(CeremonyCommand.SasDecision(ceremonyIdentity, decision));

    /// <inheritdoc/>
    public void Stop()
    {
        Thread? thread;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (state == PairingCeremonyHostState.NotStarted)
            {
                state = PairingCeremonyHostState.Stopped;
            }

            thread = ownerThread;
        }

        stopRequested = true;
        wake.Set();
        if (thread is not null && thread != Thread.CurrentThread && !thread.Join(Constants.StopTimeout))
        {
            throw new TimeoutException("The sas-pairing owner thread did not stop within its bound.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        wake.Dispose();
        startupFinished.Dispose();
    }

    /// <summary>
    /// Composes a host over the real sas-pairing package: the library's composition entry point. The
    /// host does nothing until <see cref="Start"/>.
    /// </summary>
    /// <param name="options">The detached local configuration.</param>
    /// <param name="observer">Receives requests and outcomes on the owner thread.</param>
    /// <returns>The unstarted host.</returns>
    public static IPairingCeremonyHost Create(PairingCeremonyHostOptions options, IPairingCeremonyObserver observer) =>
        new PairingCeremonyHost(options, observer, new SasPairingNativeSessionFactory());

    /// <summary>Maps a native start failure to the host failure it causes.</summary>
    /// <param name="kind">The native failure classification.</param>
    /// <returns>The host failure.</returns>
    private static PairingCeremonyFailure StartFailureFor(NativeFailureKind kind) => kind switch
    {
        NativeFailureKind.OwnershipUnavailable => PairingCeremonyFailure.AuthorityUnavailable,
        _ => RunningFailureFor(kind),
    };

    /// <summary>Maps a host-wide native failure to the host failure it causes.</summary>
    /// <param name="kind">The native failure classification.</param>
    /// <returns>The host failure.</returns>
    private static PairingCeremonyFailure RunningFailureFor(NativeFailureKind kind) => kind switch
    {
        NativeFailureKind.ProcessFatal => PairingCeremonyFailure.ProcessFatal,
        NativeFailureKind.ContractViolation => PairingCeremonyFailure.ContractViolation,
        NativeFailureKind.OwnerLoopFailed => PairingCeremonyFailure.OwnerLoopFailed,
        _ => PairingCeremonyFailure.StartFailed,
    };

    /// <summary>Whether a native failure only affects the step and attempt it happened in, rather than the whole host.</summary>
    /// <param name="kind">The native failure classification.</param>
    /// <returns><see langword="true"/> for an attempt-scoped failure.</returns>
    private static bool IsAttemptScoped(NativeFailureKind kind) =>
        kind is NativeFailureKind.OperationFailed or NativeFailureKind.RunEnded or NativeFailureKind.WritePending;

    /// <summary>Queues a decision for the owner thread while the host is running.</summary>
    /// <param name="command">The decision.</param>
    /// <returns>Whether it was queued.</returns>
    private bool TryQueue(CeremonyCommand command)
    {
        if (State != PairingCeremonyHostState.Running || !commands.Writer.TryWrite(command))
        {
            return false;
        }

        wake.Set();
        return true;
    }

    /// <summary>The owner thread: opens and attaches the native host, drives it until stopped or failed, then releases everything.</summary>
    private void RunOwner()
    {
        ISasPairingNativeSession? session = null;
        Socket? listener = null;
        try
        {
            try
            {
                session = sessionFactory.Open(options.NativeLibraryPath, options.AuthorityScope);
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(Constants.ListenerBacklog);
                var endpoint = (IPEndPoint)listener.LocalEndPoint!;
                session.AttachListener(listener, options.LocalBootstrap);
                lock (gate)
                {
                    listenerEndpoint = endpoint;
                    state = PairingCeremonyHostState.Running;
                }
            }
            catch (PairingCeremonyNativeException exception)
            {
                FailClosed(StartFailureFor(exception.Kind));
                return;
            }
            catch (SocketException)
            {
                FailClosed(PairingCeremonyFailure.StartFailed);
                return;
            }
            finally
            {
                startupFinished.Set();
            }

            DriveUntilStopped(session);
        }
        catch (PairingCeremonyNativeException exception)
        {
            FailClosed(RunningFailureFor(exception.Kind));
        }
        catch (Exception)
        {
            // An unexpected fault must never escape this raw thread and end the Host process; the
            // integration fails closed instead and does no more pairing work.
            FailClosed(PairingCeremonyFailure.OwnerLoopFailed);
        }
        finally
        {
            // Disposing the listening socket after the native library adopted it does nothing; before
            // adoption it closes it. The native host then closes its listener and every connection.
            listener?.Dispose();
            session?.Dispose();
            attempts.Clear();
            exposureRequested.Clear();
            lock (gate)
            {
                listenerEndpoint = null;
                if (state != PairingCeremonyHostState.Failed)
                {
                    state = PairingCeremonyHostState.Stopped;
                }
            }

            startupFinished.Set();
        }
    }

    /// <summary>Applies decisions, retries pending steps, and drives once per iteration until a stop is requested.</summary>
    /// <param name="session">The attached native session.</param>
    /// <exception cref="PairingCeremonyNativeException">A host-wide native failure; the caller fails closed.</exception>
    private void DriveUntilStopped(ISasPairingNativeSession session)
    {
        while (!stopRequested)
        {
            bool progressed = ApplyQueuedDecisions(session);
            progressed |= AdvancePendingSteps(session);
            NativeDriveResult drive = session.Drive();
            foreach (NativeCeremonyEvent evt in drive.Events)
            {
                Handle(evt);
            }

            if (drive.Failure is NativeFailureKind driveFailure)
            {
                throw new PairingCeremonyNativeException(driveFailure, "Drive", "OwnerLoopFailedClosed");
            }

            if (!progressed && drive.Events.Count == 0)
            {
                wake.WaitOne(Constants.IdlePause);
            }
        }
    }

    /// <summary>Follows one translated drive event: new runs, key arrival, local results, and run endings.</summary>
    /// <param name="evt">The event.</param>
    private void Handle(NativeCeremonyEvent evt)
    {
        CeremonyAttempt? attempt = null;
        if (evt.Run is not null && !attempts.TryGetValue(evt.Run, out attempt) && evt.ProtocolEvent == NativeProtocolEvent.StartAccepted)
        {
            attempt = new CeremonyAttempt(new CeremonyAttemptId(++lastAttempt), evt.Run);
            attempts.Add(evt.Run, attempt);
        }

        if (evt.Result is not null)
        {
            if (attempt is not null)
            {
                attempts.Remove(attempt.Run);
                exposureRequested.TryRemove(attempt.Id, out _);
            }

            Notify(() => observer.OnCeremonyCompletedLocally(attempt?.Id, evt.Result));
            return;
        }

        if (attempt is null)
        {
            return;
        }

        if (evt.RunEnded)
        {
            EndAttempt(attempt);
            return;
        }

        if (evt.ProtocolEvent == NativeProtocolEvent.InitiatorKey && attempt.Stage == CeremonyAttemptStage.AwaitingInitiatorKey)
        {
            attempt.Stage = CeremonyAttemptStage.AwaitingExposureAuthorization;
            exposureRequested[attempt.Id] = 0;
            Notify(() => observer.OnExposureAuthorizationRequested(attempt.Id));
        }
    }

    /// <summary>Applies every queued decision to the attempt it names; a decision naming nothing current changes nothing.</summary>
    /// <param name="session">The attached native session.</param>
    /// <returns>Whether any decision was applied.</returns>
    private bool ApplyQueuedDecisions(ISasPairingNativeSession session)
    {
        bool applied = false;
        while (commands.Reader.TryRead(out CeremonyCommand? command))
        {
            if (command.ExposureAttempt is CeremonyAttemptId exposureAttempt)
            {
                CeremonyAttempt? attempt = attempts.Values.FirstOrDefault(candidate =>
                    candidate.Id == exposureAttempt && candidate.Stage == CeremonyAttemptStage.AwaitingExposureAuthorization);
                if (attempt is not null)
                {
                    exposureRequested.TryRemove(attempt.Id, out _);
                    attempt.Stage = CeremonyAttemptStage.Exposing;
                    applied = true;
                }
            }
            else if (command.CeremonyIdentity is byte[] identity && command.Decision is SasComparisonDecision decision)
            {
                CeremonyAttempt? attempt = attempts.Values.FirstOrDefault(candidate =>
                    candidate.Stage == CeremonyAttemptStage.AwaitingSasDecision &&
                    candidate.PresentedCeremonyIdentity is not null &&
                    candidate.PresentedCeremonyIdentity.AsSpan().SequenceEqual(identity));
                if (attempt is not null)
                {
                    ApplySasDecision(session, attempt, decision);
                    applied = true;
                }
            }
        }

        return applied;
    }

    /// <summary>Applies one explicit SAS decision to the attempt whose presented identity it named.</summary>
    /// <param name="session">The attached native session.</param>
    /// <param name="attempt">The attempt.</param>
    /// <param name="decision">The decision.</param>
    private void ApplySasDecision(ISasPairingNativeSession session, CeremonyAttempt attempt, SasComparisonDecision decision)
    {
        byte[] identity = attempt.PresentedCeremonyIdentity!;
        switch (decision)
        {
            case SasComparisonDecision.Match:
                if (TryStep(attempt, () => session.ApproveSas(attempt.Run, identity)))
                {
                    attempt.Stage = CeremonyAttemptStage.EmittingBootstrapMac;
                }

                break;
            case SasComparisonDecision.Mismatch:
                if (TryStep(attempt, () => session.RejectSas(attempt.Run, identity)))
                {
                    EndAttempt(attempt);
                }

                break;
            default:
                if (TryStep(attempt, () => session.CancelSas(attempt.Run, identity)))
                {
                    EndAttempt(attempt);
                }

                break;
        }
    }

    /// <summary>Advances every attempt waiting on a local step: exposure, presentation, or Bootstrap MAC emission.</summary>
    /// <param name="session">The attached native session.</param>
    /// <returns>Whether any attempt advanced.</returns>
    private bool AdvancePendingSteps(ISasPairingNativeSession session)
    {
        bool advanced = false;
        foreach (CeremonyAttempt attempt in attempts.Values.ToArray())
        {
            switch (attempt.Stage)
            {
                case CeremonyAttemptStage.Exposing:
                    if (!attempt.ExposureAuthorized && TryStep(attempt, () => session.AuthorizeExposure(attempt.Run)))
                    {
                        attempt.ExposureAuthorized = true;
                    }

                    if (attempt.ExposureAuthorized && TryStep(attempt, () => session.ExposeKey(attempt.Run)))
                    {
                        attempt.Stage = CeremonyAttemptStage.AwaitingPresentation;
                        advanced = true;
                    }

                    break;
                case CeremonyAttemptStage.AwaitingPresentation:
                    NativeSasPresentation? presentation = null;
                    if (TryStep(attempt, () => presentation = session.Presentation(attempt.Run)) && presentation is not null)
                    {
                        attempt.PresentedCeremonyIdentity = presentation.CeremonyIdentity;
                        attempt.Stage = CeremonyAttemptStage.AwaitingSasDecision;
                        var request = new SasComparisonRequest(attempt.Id, presentation.CeremonyIdentity, presentation.DecimalDisplay);
                        Notify(() => observer.OnSasComparisonRequested(request));
                        advanced = true;
                    }

                    break;
                case CeremonyAttemptStage.EmittingBootstrapMac:
                    if (TryStep(attempt, () => session.EmitBootstrapMac(attempt.Run)))
                    {
                        attempt.Stage = CeremonyAttemptStage.AwaitingResult;
                        advanced = true;
                    }

                    break;
            }
        }

        return advanced;
    }

    /// <summary>
    /// Runs one native step for an attempt. A retained frame leaves the step for a later iteration; a
    /// run that ended or a step that failed ends only that attempt; anything host-wide propagates.
    /// </summary>
    /// <param name="attempt">The attempt the step belongs to.</param>
    /// <param name="step">The native step.</param>
    /// <returns>Whether the step ran.</returns>
    /// <exception cref="PairingCeremonyNativeException">A host-wide native failure.</exception>
    private bool TryStep(CeremonyAttempt attempt, Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (PairingCeremonyNativeException exception) when (IsAttemptScoped(exception.Kind))
        {
            if (exception.Kind != NativeFailureKind.WritePending)
            {
                EndAttempt(attempt);
            }

            return false;
        }
    }

    /// <summary>Stops following an attempt and reports that it ended without a local result.</summary>
    /// <param name="attempt">The attempt.</param>
    private void EndAttempt(CeremonyAttempt attempt)
    {
        exposureRequested.TryRemove(attempt.Id, out _);
        if (attempts.Remove(attempt.Run))
        {
            Notify(() => observer.OnAttemptEnded(attempt.Id));
        }
    }

    /// <summary>Latches the host failed and reports it; the host never restarts or recreates its native runtime.</summary>
    /// <param name="hostFailure">Why it failed.</param>
    private void FailClosed(PairingCeremonyFailure hostFailure)
    {
        lock (gate)
        {
            state = PairingCeremonyHostState.Failed;
            failure ??= hostFailure;
        }

        Notify(() => observer.OnFailed(hostFailure, ProcessRestartRequired));
    }

    /// <summary>Calls the observer, ignoring any exception so the owner thread keeps its own lifecycle.</summary>
    /// <param name="notification">The observer call.</param>
    private void Notify(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception)
        {
            // The observer contract forbids throwing; a misbehaving observer must not stop native cleanup.
        }
    }
}
