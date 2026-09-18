using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

namespace DovahLink.Host.Client.Subscription;

/// <summary>
/// One connection's own subscribed state areas and per-area recovery barrier. Created fresh per
/// connection, alongside <see cref="Authentication.PublicHelloAdmissionHandler"/>, so a reconnect
/// never inherits a previous connection's subscriptions or delivery state. Answers <c>subscribe</c>
/// and <c>snapshot_request</c> against the host-wide <see cref="IRegisteredStateAreaPolicy"/> and
/// <see cref="IStatePublicationFeed"/>. Every accepted area moves through
/// <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, <see cref="AreaDeliveryPhase.Recovering"/>, and
/// <see cref="AreaDeliveryPhase.Live"/>: establishing a baseline holds any Event above the baseline's
/// revision rather than discarding or forwarding it ahead of the baseline, and releases the held
/// Events, in arrival order, only once the connection actually admits the baseline -- per
/// <c>protocol/schema/README.md</c>'s "the host sends a snapshot before events for each accepted
/// state area." A play-context transition, or a <see cref="IStateAuthorityLifecycle.Rotated"/>
/// state-authority rotation, invalidates every area's live baseline and any Events held for it, so a
/// later Event stops forwarding until this connection obtains a fresh baseline: incremental
/// continuity from the previous <see cref="StateAuthorityId"/> is invalid until a fresh baseline is
/// established under the new one. A registered area with no authoritative value available yet never
/// hangs silently: the pending request is retried automatically the moment a value appears, restarted
/// rather than abandoned across a play-context transition or state-authority rotation, and answered
/// with an explicit, retryable <c>error</c> if none appears before its own bounded deadline.
/// </summary>
public interface IPublicStateSubscription
{
    /// <summary>
    /// Binds this subscription to the connection and session identity admission just established.
    /// Must be called exactly once, before <see cref="HandleSubscribe"/> or
    /// <see cref="HandleSnapshotRequest"/> can send anything.
    /// </summary>
    /// <param name="connectionContext">The connection to send every snapshot and event through.</param>
    /// <param name="sessionId">The admitted session identity stamped onto every message this subscription sends.</param>
    void Bind(IPublicConnectionContext connectionContext, SessionId sessionId);

    /// <summary>
    /// Answers a <c>subscribe</c> request's accept/reject decision only -- it sends nothing. Accepts
    /// each requested area that is both registered and, when it does not already have a live
    /// baseline, fits within the reserved Control/Recovery lane's remaining capacity once
    /// <paramref name="reservedControlCapacity"/> is set aside for the caller's own upcoming send;
    /// rejects every other requested area, including one that would have been registered but did not
    /// fit. Idempotent for an area this connection already has a live baseline for -- it is reported
    /// accepted again without needing a fresh baseline. An already-accepted area that lost its live
    /// baseline (a play-context transition) is treated the same as a newly accepted area for capacity
    /// purposes. Call <see cref="EstablishAcceptedBaselines"/> with the accepted areas to actually send
    /// their baselines, after the caller has sent whatever it reserved capacity for.
    /// </summary>
    /// <param name="requestedStateAreas">The state areas the client requested.</param>
    /// <param name="reservedControlCapacity">
    /// The number of Control/Recovery lane slots the caller itself is about to use for something else
    /// (typically one, for its own <c>subscription_ack</c>) once this call returns -- excluded from
    /// the budget available to accepted areas' baselines.
    /// </param>
    /// <returns>The requested areas partitioned into accepted and rejected, for the caller's own <c>subscription_ack</c>.</returns>
    (IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected) HandleSubscribe(
        IReadOnlyList<string> requestedStateAreas, int reservedControlCapacity);

    /// <summary>
    /// Sends a baseline (correlated to <paramref name="correlationMessageId"/>) for each of
    /// <paramref name="acceptedStateAreas"/> that does not already have a live baseline and has a
    /// current value available -- the send <see cref="HandleSubscribe"/> itself never performs, so a
    /// caller can guarantee its own <c>subscription_ack</c> is sent first. An area with no current
    /// value available yet is never fabricated: instead, the baseline is retained as a bounded pending
    /// request for that area, delivered automatically once a value becomes available, or answered
    /// with an explicit, retryable <c>error</c> if none does before its own bounded deadline -- an
    /// accepted subscription is never left silently waiting forever.
    /// </summary>
    /// <param name="acceptedStateAreas">The areas <see cref="HandleSubscribe"/> just reported accepted.</param>
    /// <param name="correlationMessageId">The originating <c>subscribe</c> message's own id.</param>
    void EstablishAcceptedBaselines(IReadOnlyList<string> acceptedStateAreas, string correlationMessageId);

    /// <summary>
    /// Answers a <c>snapshot_request</c>: sends a fresh baseline for <paramref name="stateArea"/>,
    /// correlated to <paramref name="correlationMessageId"/>, when it is registered and a current
    /// value is available, arming that area's event-forwarding gate only once the connection actually
    /// admits the baseline -- the same successful-admission requirement <see cref="HandleSubscribe"/>
    /// applies. Never silently hangs when the area is registered but no value is available yet: the
    /// request is retained as a bounded pending baseline for that area (superseding a previous still-
    /// pending one for the same area, if any) and delivered automatically once a value becomes
    /// available, or answered with an explicit, retryable <c>error</c> if none does before its own
    /// bounded deadline elapses.
    /// </summary>
    /// <param name="stateArea">The requested state area.</param>
    /// <param name="correlationMessageId">The <c>snapshot_request</c>'s own message id.</param>
    /// <returns><see langword="true"/> when <paramref name="stateArea"/> is registered; otherwise <see langword="false"/>, for the caller's own rejection.</returns>
    bool HandleSnapshotRequest(string stateArea, string correlationMessageId);

    /// <summary>Stops forwarding events to this connection. Idempotent; safe to call even if no subscription was ever accepted.</summary>
    void Unsubscribe();
}

/// <inheritdoc cref="IPublicStateSubscription"/>
public sealed class PublicStateSubscription : IPublicStateSubscription
{
    /// <summary>The host-wide bounded set of state areas currently served.</summary>
    private readonly IRegisteredStateAreaPolicy registeredStateAreaPolicy;

    /// <summary>The host-wide, domain-agnostic push source snapshots and events are read from.</summary>
    private readonly IStatePublicationFeed feed;

    /// <summary>Encodes every <c>state_snapshot</c>/<c>state_event</c> message this subscription sends.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Supplies the <c>playContextId</c> stamped onto every message this subscription sends.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Signals a state-authority rotation, which invalidates every area's live baseline the same way a play-context transition does.</summary>
    private readonly IStateAuthorityLifecycle stateAuthorityLifecycle;

    /// <summary>How long a pending <c>snapshot_request</c>/<c>subscribe</c> baseline waits for an authoritative value before <see cref="FailPendingBaselineOnTimeoutAsync"/> answers it with an explicit error.</summary>
    private readonly TimeSpan pendingBaselineDeadline;

    /// <summary>Guards every mutable field below against concurrent access from <see cref="OnEventOccurred"/> and the read loop's own thread.</summary>
    private readonly object gate = new();

    /// <summary>Every state area this connection has accepted via <see cref="HandleSubscribe"/>.</summary>
    private readonly HashSet<StateAreaId> acceptedAreas = [];

    /// <summary>
    /// Every accepted state area's own recovery-barrier bookkeeping, created on first use and never
    /// removed for the lifetime of this subscription. Absence is equivalent to
    /// <see cref="AreaDeliveryPhase.AwaitingBaseline"/>.
    /// </summary>
    private readonly Dictionary<StateAreaId, AreaState> areaStates = [];

    /// <summary>The connection to send through, once <see cref="Bind"/> has been called.</summary>
    private IPublicConnectionContext? connectionContext;

    /// <summary>The admitted session identity to stamp onto every message, once <see cref="Bind"/> has been called.</summary>
    private SessionId? sessionId;

    /// <summary>
    /// Whether this subscription currently owns a live registration on <see cref="feed"/>'s,
    /// <see cref="playContextTracker"/>'s, and <see cref="stateAuthorityLifecycle"/>'s events. Guards
    /// <see cref="Bind"/> and <see cref="Unsubscribe"/>
    /// against a duplicate call each: a connection whose admission never completes (for example a
    /// failed WebSocket handshake) must never register these handlers at all, since nothing would
    /// ever call <see cref="Unsubscribe"/> to remove them, and a second <see cref="Bind"/> call must
    /// never subscribe a second time, which would otherwise dispatch every later event or transition
    /// to this instance twice.
    /// </summary>
    private bool subscribedToExternalEvents;

    /// <summary>Creates a subscription bound to no connection yet, using the production <see cref="Constants.PendingBaselineDeadline"/>; call <see cref="Bind"/> once admission completes.</summary>
    /// <param name="registeredStateAreaPolicy">The host-wide bounded set of state areas currently served.</param>
    /// <param name="feed">The host-wide, domain-agnostic push source snapshots and events are read from.</param>
    /// <param name="codec">Encodes every message this subscription sends.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every message this subscription sends.</param>
    /// <param name="stateAuthorityLifecycle">Signals a state-authority rotation, which invalidates every area's live baseline the same way a play-context transition does.</param>
    public PublicStateSubscription(
        IRegisteredStateAreaPolicy registeredStateAreaPolicy,
        IStatePublicationFeed feed,
        IPublicEnvelopeCodec codec,
        IPlayContextTracker playContextTracker,
        IStateAuthorityLifecycle stateAuthorityLifecycle)
        : this(registeredStateAreaPolicy, feed, codec, playContextTracker, stateAuthorityLifecycle, Constants.PendingBaselineDeadline)
    {
    }

    /// <summary>Creates a subscription over an explicit pending-baseline deadline. Exposed for tests that need a faster-than-production bound.</summary>
    /// <param name="registeredStateAreaPolicy">The host-wide bounded set of state areas currently served.</param>
    /// <param name="feed">The host-wide, domain-agnostic push source snapshots and events are read from.</param>
    /// <param name="codec">Encodes every message this subscription sends.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every message this subscription sends.</param>
    /// <param name="stateAuthorityLifecycle">Signals a state-authority rotation, which invalidates every area's live baseline the same way a play-context transition does.</param>
    /// <param name="pendingBaselineDeadline">How long a pending baseline waits for an authoritative value before it is answered with an explicit error.</param>
    internal PublicStateSubscription(
        IRegisteredStateAreaPolicy registeredStateAreaPolicy,
        IStatePublicationFeed feed,
        IPublicEnvelopeCodec codec,
        IPlayContextTracker playContextTracker,
        IStateAuthorityLifecycle stateAuthorityLifecycle,
        TimeSpan pendingBaselineDeadline)
    {
        this.registeredStateAreaPolicy = registeredStateAreaPolicy;
        this.feed = feed;
        this.codec = codec;
        this.playContextTracker = playContextTracker;
        this.stateAuthorityLifecycle = stateAuthorityLifecycle;
        this.pendingBaselineDeadline = pendingBaselineDeadline;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Also registers this subscription's <see cref="feed"/> and <see cref="playContextTracker"/>
    /// event handlers, the first time this is called for this instance -- deferred from the
    /// constructor to here so a connection whose admission never completes never roots this
    /// subscription on either long-lived event source. A second call is a no-op for that
    /// registration, but still updates <see cref="connectionContext"/> and <see cref="sessionId"/>.
    /// </remarks>
    public void Bind(IPublicConnectionContext connectionContext, SessionId sessionId)
    {
        lock (gate)
        {
            this.connectionContext = connectionContext;
            this.sessionId = sessionId;

            if (!subscribedToExternalEvents)
            {
                feed.EventOccurred += OnEventOccurred;
                feed.SnapshotChanged += OnSnapshotChanged;
                playContextTracker.Transitioned += OnPlayContextTransitioned;
                stateAuthorityLifecycle.Rotated += OnStateAuthorityRotated;
                subscribedToExternalEvents = true;
            }
        }
    }

    /// <inheritdoc/>
    public (IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected) HandleSubscribe(
        IReadOnlyList<string> requestedStateAreas, int reservedControlCapacity)
    {
        List<string> accepted = [];
        List<string> rejected = [];

        lock (gate)
        {
            int rawCapacity = connectionContext?.RemainingOutboundCapacity(PublicOutboundLane.ControlOrRecovery) ?? int.MaxValue;
            int budget = Math.Max(0, rawCapacity - reservedControlCapacity);

            foreach (string requested in requestedStateAreas)
            {
                var areaId = new StateAreaId(requested);
                if (!registeredStateAreaPolicy.IsRegistered(areaId))
                {
                    rejected.Add(requested);
                    continue;
                }

                bool needsBaseline = !areaStates.TryGetValue(areaId, out AreaState? state) || state.Phase != AreaDeliveryPhase.Live;
                if (needsBaseline)
                {
                    if (budget <= 0)
                    {
                        rejected.Add(requested);
                        continue;
                    }

                    budget--;
                }

                accepted.Add(requested);
                acceptedAreas.Add(areaId);
            }
        }

        return (accepted, rejected);
    }

    /// <inheritdoc/>
    public void EstablishAcceptedBaselines(IReadOnlyList<string> acceptedStateAreas, string correlationMessageId)
    {
        List<StateAreaId> areasNeedingBaseline = [];

        lock (gate)
        {
            foreach (string accepted in acceptedStateAreas)
            {
                var areaId = new StateAreaId(accepted);
                bool needsBaseline = !areaStates.TryGetValue(areaId, out AreaState? state) || state.Phase != AreaDeliveryPhase.Live;
                if (needsBaseline)
                {
                    areasNeedingBaseline.Add(areaId);
                }
            }
        }

        foreach (StateAreaId areaId in areasNeedingBaseline)
        {
            TryEstablishBaseline(areaId, correlationMessageId);
        }
    }

    /// <inheritdoc/>
    public bool HandleSnapshotRequest(string stateArea, string correlationMessageId)
    {
        var areaId = new StateAreaId(stateArea);
        if (!registeredStateAreaPolicy.IsRegistered(areaId))
        {
            return false;
        }

        TryEstablishBaseline(areaId, correlationMessageId);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A no-op when <see cref="Bind"/> was never called (so no registration was ever made) or when
    /// this has already been called once for this instance. Also cancels every area's own armed
    /// pending-baseline deadline: this connection is going away, so nothing should still attempt to
    /// answer it once this call returns.
    /// </remarks>
    public void Unsubscribe()
    {
        lock (gate)
        {
            if (!subscribedToExternalEvents)
            {
                return;
            }

            feed.EventOccurred -= OnEventOccurred;
            feed.SnapshotChanged -= OnSnapshotChanged;
            playContextTracker.Transitioned -= OnPlayContextTransitioned;
            stateAuthorityLifecycle.Rotated -= OnStateAuthorityRotated;
            subscribedToExternalEvents = false;

            foreach (AreaState state in areaStates.Values)
            {
                CancelPendingBaselineDeadlineLocked(state);
            }
        }
    }

    /// <summary>
    /// Invalidates every area's live baseline and abandons every in-progress recovery: a baseline (or
    /// an Event held while one establishes) that belongs to one play context must never be treated as
    /// current once the context changes, so this connection must obtain a fresh baseline -- via
    /// <see cref="HandleSnapshotRequest"/>, or a later <see cref="HandleSubscribe"/> for the same area
    /// -- before it forwards another Event for that area. Bumping each area's recovery epoch here
    /// ensures a <see cref="TryEstablishBaseline"/> call already in flight for the old context is
    /// ignored when it completes, rather than incorrectly committing a stale baseline live. Leaves
    /// <see cref="acceptedAreas"/> untouched; an already-accepted area does not need to be
    /// re-subscribed.
    /// </summary>
    /// <param name="transition">The transition the tracker just committed.</param>
    private void OnPlayContextTransitioned(PlayContextTransition transition)
    {
        lock (gate)
        {
            InvalidateAllAreasUnderGate();
        }
    }

    /// <summary>
    /// Invalidates every area's live baseline and abandons every in-progress recovery, the same way
    /// <see cref="OnPlayContextTransitioned"/> does for a play-context transition: incremental
    /// continuity from the previous <see cref="StateAuthorityId"/> is invalid outright once it
    /// rotates, so this connection must obtain a fresh baseline before it forwards another Event for
    /// any area. Bumping each area's
    /// recovery epoch here ensures a <see cref="TryEstablishBaseline"/> call already in flight under
    /// the old value is ignored when it completes, rather than incorrectly committing a stale
    /// baseline live.
    /// </summary>
    /// <param name="rotatedTo">The newly minted <see cref="StateAuthorityId"/>.</param>
    private void OnStateAuthorityRotated(StateAuthorityId rotatedTo)
    {
        lock (gate)
        {
            InvalidateAllAreasUnderGate();
        }
    }

    /// <summary>
    /// Resets every area's recovery-barrier bookkeeping to <see cref="AreaDeliveryPhase.AwaitingBaseline"/>,
    /// discarding any held Events or buffered pending Snapshot and bumping the recovery epoch so an
    /// in-flight <see cref="TryEstablishBaseline"/> attempt from before this call is ignored when it
    /// completes. A genuinely pending request (a non-<see langword="null"/>
    /// <see cref="AreaState.RecoveryCorrelationMessageId"/>) is restarted rather than abandoned: its
    /// own bounded deadline is re-armed fresh under the new context/authority, but -- unlike
    /// <see cref="OnSnapshotChanged"/>'s wake -- this never synchronously re-queries <see cref="feed"/>
    /// for a value here, since a value already sitting in the feed at this exact instant belongs to
    /// whatever just stopped being current and must not be allowed to satisfy this request; only a
    /// value the feed genuinely publishes afterward, under the new context/authority, ever can. Must
    /// be called with <see cref="gate"/> already held by the calling thread.
    /// </summary>
    private void InvalidateAllAreasUnderGate()
    {
        foreach ((StateAreaId areaId, AreaState state) in areaStates)
        {
            CancelPendingBaselineDeadlineLocked(state);
            state.Phase = AreaDeliveryPhase.AwaitingBaseline;
            state.BarrierRevision = null;
            state.HeldEvents.Clear();
            state.PendingSnapshot = null;
            long myEpoch = ++state.RecoveryEpoch;
            if (state.RecoveryCorrelationMessageId is string correlationMessageId)
            {
                ArmPendingBaselineDeadlineLocked(areaId, state, myEpoch, correlationMessageId);
            }
        }
    }

    /// <summary>
    /// Routes <paramref name="eventPublication"/> for this connection according to its state area's
    /// current recovery-barrier phase: discarded while <see cref="AreaDeliveryPhase.AwaitingBaseline"/>;
    /// while <see cref="AreaDeliveryPhase.Recovering"/>, held if the barrier revision is not yet known
    /// (a baseline fetch is still in flight) or the Event is above the barrier once it is known,
    /// discarded if at or below it (abandoning the held set and re-baselining from the newest
    /// authoritative snapshot, correlated to the same message that started the abandoned attempt, if
    /// the bounded hold fills up); forwarded immediately, still under
    /// <see cref="gate"/>, while <see cref="AreaDeliveryPhase.Live"/>. Also discarded outright,
    /// regardless of phase, when its own captured play-context generation does not match the tracker's
    /// current one -- a stale value from before a transition this subscription has not yet been told
    /// to forward.
    /// </summary>
    /// <param name="eventPublication">The event the feed just published.</param>
    private void OnEventOccurred(StateEventPublication eventPublication)
    {
        bool needsReBaseline = false;
        string? reBaselineCorrelationMessageId = null;

        lock (gate)
        {
            if (!acceptedAreas.Contains(eventPublication.StateArea))
            {
                return;
            }

            if (eventPublication.PlayContextGeneration != playContextTracker.GetSnapshot().TransitionGeneration)
            {
                return;
            }

            AreaState state = GetOrCreateAreaState(eventPublication.StateArea);
            switch (state.Phase)
            {
                case AreaDeliveryPhase.AwaitingBaseline:
                    break;

                case AreaDeliveryPhase.Recovering:
                    if (state.BarrierRevision is RevisionNumber barrier && eventPublication.Revision.Value <= barrier.Value)
                    {
                        break; // superseded by the barrier
                    }

                    if (state.HeldEvents.Count >= Constants.MaxHeldRecoveryEventsPerArea)
                    {
                        reBaselineCorrelationMessageId = state.RecoveryCorrelationMessageId;
                        state.HeldEvents.Clear();
                        state.RecoveryEpoch++;
                        state.Phase = AreaDeliveryPhase.AwaitingBaseline;
                        state.BarrierRevision = null;
                        needsReBaseline = true;
                    }
                    else
                    {
                        state.HeldEvents.Add(eventPublication);
                    }

                    break;

                case AreaDeliveryPhase.Live:
                    if (eventPublication.PlayContextGeneration == state.PlayContextGeneration
                        && connectionContext is not null && sessionId is not null)
                    {
                        SendEventUnderGate(connectionContext, sessionId.Value, eventPublication);
                    }

                    break;
            }
        }

        if (needsReBaseline)
        {
            TryEstablishBaseline(eventPublication.StateArea, reBaselineCorrelationMessageId!);
        }
    }

    /// <summary>
    /// Routes <paramref name="snapshotPublication"/> for this connection according to its state
    /// area's current recovery-barrier phase: while <see cref="AreaDeliveryPhase.AwaitingBaseline"/>,
    /// wakes and retries a genuinely pending request (a non-<see langword="null"/>
    /// <see cref="AreaState.RecoveryCorrelationMessageId"/>) via <see cref="TryEstablishBaseline"/>
    /// rather than leaving it to wait out its own deadline for a value that has, in fact, just
    /// arrived -- a no-op when no request is pending; while <see cref="AreaDeliveryPhase.Recovering"/>,
    /// replaces any previously buffered pending value for the area rather than queuing a second one --
    /// unlike an Event, a Snapshot is a complete replacement value, so only the newest one received
    /// during recovery is ever worth keeping; sent immediately, still under <see cref="gate"/>, while
    /// <see cref="AreaDeliveryPhase.Live"/>, on the Data lane's keyed-replaceable slot rather than the
    /// Control/Recovery lane baselines use. Also discarded outright, regardless of phase, when its own
    /// captured play-context generation does not match the tracker's current one, the same stale-value
    /// rule <see cref="OnEventOccurred"/> applies. Processed even for an area this connection never
    /// accepted via <see cref="HandleSubscribe"/> as long as it has a genuinely pending request -- a
    /// bare <c>snapshot_request</c> must still be woken by this same mechanism -- but such an area can
    /// only ever be waiting in <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, never actually reach
    /// <see cref="AreaDeliveryPhase.Recovering"/> or <see cref="AreaDeliveryPhase.Live"/> ongoing
    /// forwarding here, which remains exclusively gated on acceptance.
    /// </summary>
    /// <param name="snapshotPublication">The snapshot value the feed just published.</param>
    private void OnSnapshotChanged(StateSnapshotPublication snapshotPublication)
    {
        string? pendingCorrelationMessageId = null;

        lock (gate)
        {
            bool hasPendingRequest = areaStates.TryGetValue(snapshotPublication.StateArea, out AreaState? existingState)
                && existingState.Phase == AreaDeliveryPhase.AwaitingBaseline
                && existingState.RecoveryCorrelationMessageId is not null;
            if (!acceptedAreas.Contains(snapshotPublication.StateArea) && !hasPendingRequest)
            {
                return;
            }

            if (snapshotPublication.PlayContextGeneration != playContextTracker.GetSnapshot().TransitionGeneration)
            {
                return;
            }

            AreaState state = GetOrCreateAreaState(snapshotPublication.StateArea);
            switch (state.Phase)
            {
                case AreaDeliveryPhase.AwaitingBaseline:
                    pendingCorrelationMessageId = state.RecoveryCorrelationMessageId;
                    break;

                case AreaDeliveryPhase.Recovering:
                    state.PendingSnapshot = snapshotPublication;
                    break;

                case AreaDeliveryPhase.Live:
                    if (snapshotPublication.PlayContextGeneration == state.PlayContextGeneration
                        && connectionContext is not null && sessionId is not null)
                    {
                        SendSnapshotUnderGate(connectionContext, sessionId.Value, snapshotPublication);
                    }

                    break;
            }
        }

        if (pendingCorrelationMessageId is not null)
        {
            TryEstablishBaseline(snapshotPublication.StateArea, pendingCorrelationMessageId);
        }
    }

    /// <summary>
    /// Establishes a fresh baseline for <paramref name="areaId"/> through the reserved Control/Recovery
    /// lane. Enters <see cref="AreaDeliveryPhase.Recovering"/> under a fresh recovery epoch, with the
    /// barrier revision still unknown, before reading <paramref name="areaId"/>'s current value from
    /// <see cref="feed"/> -- deliberately outside <see cref="gate"/>, since a future real feed
    /// implementation must not be called while this subscription's own lock is held, to avoid a
    /// lock-order inversion against whatever synchronization the authoritative state store uses
    /// internally. An Event for this area arriving during that fetch observes the unknown barrier and
    /// is held rather than discarded, per <see cref="OnEventOccurred"/>. Once the fetch returns, every
    /// remaining step -- validating the recovery epoch and the value's own play-context generation,
    /// recording the barrier, encoding, admitting the baseline onto the Control/Recovery lane, and
    /// draining every held Event above the barrier -- runs under <see cref="gate"/>, so nothing else
    /// can observe or mutate this area's state until <see cref="AreaDeliveryPhase.Live"/> commits (or
    /// this attempt is abandoned). A later recovery attempt, a held-Event overflow, or a play-context
    /// transition bumps the area's recovery epoch and makes this call's eventual result a no-op at
    /// whichever validation point next observes the mismatch. Falls back to
    /// <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, discarding any held Events and any buffered
    /// pending Snapshot, when no current value is available, the fetched value's play-context
    /// generation is already stale, or admission fails. Once this baseline commits Live, a Snapshot
    /// buffered while <see cref="AreaDeliveryPhase.Recovering"/> (see <see cref="OnSnapshotChanged"/>)
    /// is discarded if superseded by this same baseline, or sent if it is newer. Makes no change at
    /// all when this subscription is not currently bound to a connection.
    /// </summary>
    /// <param name="areaId">The state area to establish a baseline for.</param>
    /// <param name="correlationMessageId">
    /// The message id this baseline correlates to -- the originating <c>subscribe</c> or
    /// <c>snapshot_request</c>'s own id. Recorded on the area's <see cref="AreaState.RecoveryCorrelationMessageId"/>
    /// so a later held-Event overflow's re-baseline (see <see cref="OnEventOccurred"/>) can reuse it
    /// instead of inventing a correlation no client request ever made.
    /// </param>
    private void TryEstablishBaseline(StateAreaId areaId, string correlationMessageId)
    {
        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        long myEpoch;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
            if (currentConnectionContext is null || currentSessionId is null)
            {
                return;
            }

            AreaState state = GetOrCreateAreaState(areaId);
            // This fresh attempt supersedes anything a previous pending attempt was still waiting on.
            CancelPendingBaselineDeadlineLocked(state);
            state.Phase = AreaDeliveryPhase.Recovering;
            state.BarrierRevision = null;
            state.RecoveryCorrelationMessageId = correlationMessageId;
            myEpoch = ++state.RecoveryEpoch;
        }

        bool hasSnapshot = feed.TryGetSnapshot(areaId, out StateSnapshotPublication? snapshot);

        byte[]? bytes = null;
        lock (gate)
        {
            if (!areaStates.TryGetValue(areaId, out AreaState? state) || state.RecoveryEpoch != myEpoch)
            {
                return;
            }

            if (!hasSnapshot || snapshot!.PlayContextGeneration != playContextTracker.GetSnapshot().TransitionGeneration)
            {
                FallBackToAwaitingBaselineAndArmDeadlineLocked(areaId, state, myEpoch, correlationMessageId);
                return;
            }

            state.BarrierRevision = snapshot.Revision;
            state.PlayContextGeneration = snapshot.PlayContextGeneration;
            state.HeldEvents.RemoveAll(held => held.Revision.Value <= snapshot.Revision.Value);

            var payload = new StateSnapshotPayload
            {
                StateArea = areaId.Value,
                Revision = snapshot.Revision.Value,
                OccurredAt = snapshot.OccurredAt,
                Data = snapshot.Data,
            };
            bytes = codec.Encode(
                PublicMessageType.StateSnapshot,
                NewMessageId(),
                currentSessionId.Value.ToString(),
                correlationMessageId,
                snapshot.PlayContextId?.ToString(),
                null,
                payload);
        }

        if (bytes is null)
        {
            return;
        }

        lock (gate)
        {
            if (!areaStates.TryGetValue(areaId, out AreaState? state) || state.RecoveryEpoch != myEpoch)
            {
                return;
            }

            if (!currentConnectionContext.TrySend(bytes, PublicOutboundLane.ControlOrRecovery))
            {
                FallBackToAwaitingBaselineAndArmDeadlineLocked(areaId, state, myEpoch, correlationMessageId);
                return;
            }

            // Drains the live collection, not a copy: a reentrant Event admitted into HeldEvents while
            // sending one of these (the transport contract precludes this in production, but a test
            // may deliberately exercise it) is picked up by this same loop rather than lost.
            while (state.HeldEvents.Count > 0)
            {
                StateEventPublication next = state.HeldEvents[0];
                state.HeldEvents.RemoveAt(0);
                SendEventUnderGate(currentConnectionContext, currentSessionId.Value, next);
            }

            // Re-validated rather than assumed: a reentrant abandonment during the send or the drain
            // above must not be overwritten by this attempt committing Live regardless.
            if (state.RecoveryEpoch == myEpoch)
            {
                state.Phase = AreaDeliveryPhase.Live;

                // A Snapshot buffered while Recovering is a complete replacement value, not a delta:
                // superseded outright by this same baseline it raced against, or, if newer, the area's
                // true current value that must still reach the client.
                StateSnapshotPublication? pendingSnapshot = state.PendingSnapshot;
                state.PendingSnapshot = null;
                if (pendingSnapshot is StateSnapshotPublication pending && pending.Revision.Value > snapshot!.Revision.Value)
                {
                    SendSnapshotUnderGate(currentConnectionContext, currentSessionId.Value, pending);
                }
            }
        }
    }

    /// <summary>
    /// Falls back <paramref name="state"/> to <see cref="AreaDeliveryPhase.AwaitingBaseline"/>,
    /// discarding any held Events and any buffered pending Snapshot, then arms a fresh bounded
    /// deadline for <paramref name="correlationMessageId"/>: <see cref="OnSnapshotChanged"/> wakes and
    /// retries this same pending request the moment a matching authoritative value appears, but if
    /// none does before the deadline elapses, an explicit
    /// <see cref="PublicProtocolErrorCode.TemporarilyUnavailable"/> error answers it instead of
    /// leaving the client waiting forever. Must be called with <see cref="gate"/> already held by the
    /// calling thread.
    /// </summary>
    /// <param name="areaId">The state area falling back to <see cref="AreaDeliveryPhase.AwaitingBaseline"/>.</param>
    /// <param name="state">That area's own recovery-barrier bookkeeping.</param>
    /// <param name="myEpoch">The recovery attempt this fallback belongs to, so a later superseding attempt makes the armed deadline a no-op.</param>
    /// <param name="correlationMessageId">The still-pending request's own message id.</param>
    private void FallBackToAwaitingBaselineAndArmDeadlineLocked(StateAreaId areaId, AreaState state, long myEpoch, string correlationMessageId)
    {
        state.Phase = AreaDeliveryPhase.AwaitingBaseline;
        state.BarrierRevision = null;
        state.HeldEvents.Clear();
        state.PendingSnapshot = null;
        ArmPendingBaselineDeadlineLocked(areaId, state, myEpoch, correlationMessageId);
    }

    /// <summary>
    /// Arms a fresh bounded deadline on <paramref name="state"/> for <paramref name="correlationMessageId"/>:
    /// <see cref="OnSnapshotChanged"/> wakes and retries this same pending request the moment a
    /// matching authoritative value appears, but if none does before the deadline elapses, an
    /// explicit <see cref="PublicProtocolErrorCode.TemporarilyUnavailable"/> error answers it instead
    /// of leaving the client waiting forever. Superseding any deadline this area already had armed is
    /// the caller's own responsibility; this always installs a fresh one unconditionally. Must be
    /// called with <see cref="gate"/> already held by the calling thread.
    /// </summary>
    /// <param name="areaId">The state area this deadline is armed for.</param>
    /// <param name="state">That area's own recovery-barrier bookkeeping.</param>
    /// <param name="myEpoch">The recovery attempt this deadline belongs to, so a later superseding attempt makes it a no-op.</param>
    /// <param name="correlationMessageId">The still-pending request's own message id.</param>
    private void ArmPendingBaselineDeadlineLocked(StateAreaId areaId, AreaState state, long myEpoch, string correlationMessageId)
    {
        var deadlineCancellation = new CancellationTokenSource();
        state.PendingBaselineDeadlineCancellation = deadlineCancellation;
        _ = FailPendingBaselineOnTimeoutAsync(areaId, myEpoch, correlationMessageId, deadlineCancellation.Token);
    }

    /// <summary>
    /// Cancels and disposes <paramref name="state"/>'s own armed pending-baseline deadline, if any,
    /// and clears the field. A harmless no-op when none is armed. Must be called with
    /// <see cref="gate"/> already held by the calling thread.
    /// </summary>
    /// <param name="state">The area whose armed deadline, if any, is being superseded or resolved.</param>
    private static void CancelPendingBaselineDeadlineLocked(AreaState state)
    {
        if (state.PendingBaselineDeadlineCancellation is CancellationTokenSource deadlineCancellation)
        {
            deadlineCancellation.Cancel();
            deadlineCancellation.Dispose();
            state.PendingBaselineDeadlineCancellation = null;
        }
    }

    /// <summary>
    /// Waits <see cref="pendingBaselineDeadline"/>, then, only if <paramref name="areaId"/>'s
    /// recovery attempt is still exactly <paramref name="myEpoch"/> and still
    /// <see cref="AreaDeliveryPhase.AwaitingBaseline"/> -- meaning nothing has satisfied, superseded,
    /// or otherwise resolved this specific pending request in the meantime -- answers it with an
    /// explicit <see cref="PublicProtocolErrorCode.TemporarilyUnavailable"/> error and clears the
    /// pending correlation, so a value that arrives afterward is not mistaken for still owing this
    /// request a reply. A cancelled wait (superseded, resolved, or this subscription unsubscribed) is
    /// a silent no-op.
    /// </summary>
    /// <param name="areaId">The state area this deadline was armed for.</param>
    /// <param name="myEpoch">The recovery attempt this deadline belongs to.</param>
    /// <param name="correlationMessageId">The still-pending request's own message id, sent as the error's own correlation.</param>
    /// <param name="cancellationToken">Cancelled the moment this specific deadline is superseded or resolved.</param>
    private async Task FailPendingBaselineOnTimeoutAsync(StateAreaId areaId, long myEpoch, string correlationMessageId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(pendingBaselineDeadline, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
            if (currentConnectionContext is null || currentSessionId is null
                || !areaStates.TryGetValue(areaId, out AreaState? state)
                || state.RecoveryEpoch != myEpoch
                || state.Phase != AreaDeliveryPhase.AwaitingBaseline)
            {
                return;
            }

            state.RecoveryCorrelationMessageId = null;
            state.PendingBaselineDeadlineCancellation?.Dispose();
            state.PendingBaselineDeadlineCancellation = null;
        }

        SendTemporarilyUnavailableError(currentConnectionContext, currentSessionId.Value, correlationMessageId);
    }

    /// <summary>
    /// Encodes and sends an <c>error</c> message reporting <see cref="PublicProtocolErrorCode.TemporarilyUnavailable"/>,
    /// correlated to <paramref name="correlationMessageId"/> and marked retryable: a pending
    /// <c>snapshot_request</c> or <c>subscribe</c> baseline that never became available before its
    /// own bounded deadline elapsed, per <see cref="FailPendingBaselineOnTimeoutAsync"/>.
    /// </summary>
    /// <param name="targetConnectionContext">The connection to send through.</param>
    /// <param name="targetSessionId">The session identity to stamp onto the message.</param>
    /// <param name="correlationMessageId">The originating request's own message id.</param>
    private void SendTemporarilyUnavailableError(IPublicConnectionContext targetConnectionContext, SessionId targetSessionId, string correlationMessageId)
    {
        var payload = new ErrorPayload
        {
            Code = PublicProtocolErrorCode.TemporarilyUnavailable,
            Message = "No authoritative baseline is available yet for this state area.",
            Retryable = true,
        };
        PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();
        byte[] bytes = codec.Encode(
            PublicMessageType.Error,
            NewMessageId(),
            targetSessionId.ToString(),
            correlationMessageId,
            snapshot.Current?.ToString(),
            null,
            payload);
        targetConnectionContext.TrySend(bytes, PublicOutboundLane.ControlOrRecovery);
    }

    /// <summary>
    /// Encodes and sends one Event on the Data lane, labeled with its own captured play context.
    /// Must be called with <see cref="gate"/> already held by the calling thread.
    /// </summary>
    /// <param name="targetConnectionContext">The connection to send through.</param>
    /// <param name="targetSessionId">The session identity to stamp onto the message.</param>
    /// <param name="eventPublication">The event to send.</param>
    private void SendEventUnderGate(IPublicConnectionContext targetConnectionContext, SessionId targetSessionId, StateEventPublication eventPublication)
    {
        var payload = new StateEventPayload
        {
            StateArea = eventPublication.StateArea.Value,
            BaseRevision = eventPublication.BaseRevision.Value,
            Revision = eventPublication.Revision.Value,
            OccurredAt = eventPublication.OccurredAt,
            Data = eventPublication.Data,
        };
        byte[] bytes = codec.Encode(
            PublicMessageType.StateEvent,
            NewMessageId(),
            targetSessionId.ToString(),
            null,
            eventPublication.PlayContextId?.ToString(),
            null,
            payload);
        targetConnectionContext.TrySend(bytes, PublicOutboundLane.Data);
    }

    /// <summary>
    /// Encodes and sends one Snapshot on the Data lane's keyed-replaceable slot, labeled with its own
    /// captured play context. Unlike <see cref="SendEventUnderGate"/>, this goes through
    /// <see cref="IPublicConnectionContext.TrySendSnapshot"/> rather than <see cref="IPublicConnectionContext.TrySend"/>:
    /// a Snapshot pushed here is an ordinary replaceable value, distinct from the Control/Recovery-lane
    /// baseline <see cref="TryEstablishBaseline"/> sends. Must be called with <see cref="gate"/>
    /// already held by the calling thread.
    /// </summary>
    /// <param name="targetConnectionContext">The connection to send through.</param>
    /// <param name="targetSessionId">The session identity to stamp onto the message.</param>
    /// <param name="snapshotPublication">The snapshot value to send.</param>
    private void SendSnapshotUnderGate(IPublicConnectionContext targetConnectionContext, SessionId targetSessionId, StateSnapshotPublication snapshotPublication)
    {
        var payload = new StateSnapshotPayload
        {
            StateArea = snapshotPublication.StateArea.Value,
            Revision = snapshotPublication.Revision.Value,
            OccurredAt = snapshotPublication.OccurredAt,
            Data = snapshotPublication.Data,
        };
        byte[] bytes = codec.Encode(
            PublicMessageType.StateSnapshot,
            NewMessageId(),
            targetSessionId.ToString(),
            null,
            snapshotPublication.PlayContextId?.ToString(),
            null,
            payload);
        targetConnectionContext.TrySendSnapshot(snapshotPublication.StateArea, bytes);
    }

    /// <summary>Returns <paramref name="areaId"/>'s recovery-barrier bookkeeping, creating it on first use. Must be called under <see cref="gate"/>.</summary>
    /// <param name="areaId">The state area to look up or create bookkeeping for.</param>
    private AreaState GetOrCreateAreaState(StateAreaId areaId)
    {
        if (!areaStates.TryGetValue(areaId, out AreaState? state))
        {
            state = new AreaState();
            areaStates[areaId] = state;
        }

        return state;
    }

    /// <summary>Generates a fresh, cryptographically random host-originated message identifier.</summary>
    private static string NewMessageId() => Guid.NewGuid().ToString();

    /// <summary>
    /// One state area's own recovery-barrier bookkeeping: current phase, the baseline revision and
    /// play-context generation the current phase was established under, a monotonically increasing
    /// token identifying the most recent recovery attempt, any Event held above the barrier revision
    /// while <see cref="AreaDeliveryPhase.Recovering"/>, and any Snapshot buffered during that same
    /// phase. All access is guarded by the owning subscription's own <see cref="gate"/>; this type
    /// performs no synchronization of its own.
    /// </summary>
    private sealed class AreaState
    {
        /// <summary>This area's current delivery phase.</summary>
        public AreaDeliveryPhase Phase = AreaDeliveryPhase.AwaitingBaseline;

        /// <summary>
        /// The current or most recently established baseline's revision. <see langword="null"/> while
        /// <see cref="Phase"/> is <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, or while
        /// <see cref="AreaDeliveryPhase.Recovering"/> before the baseline fetch this attempt is waiting
        /// on has returned -- during which every Event for the area is held rather than compared
        /// against a not-yet-known barrier.
        /// </summary>
        public RevisionNumber? BarrierRevision;

        /// <summary>
        /// The play-context transition generation the current or establishing baseline belongs to.
        /// Meaningful only while <see cref="Phase"/> is <see cref="AreaDeliveryPhase.Recovering"/> or
        /// <see cref="AreaDeliveryPhase.Live"/>.
        /// </summary>
        public long PlayContextGeneration;

        /// <summary>
        /// Identifies the most recent recovery attempt for this area, incremented every time a new
        /// attempt begins (including one triggered by held-Event overflow) or a play-context
        /// transition abandons the current one. A <see cref="TryEstablishBaseline"/> call whose
        /// captured value no longer matches this field belongs to a superseded attempt and its result
        /// is ignored.
        /// </summary>
        public long RecoveryEpoch;

        /// <summary>
        /// Every Event received above <see cref="BarrierRevision"/> while <see cref="Phase"/> is
        /// <see cref="AreaDeliveryPhase.Recovering"/>, in arrival order, awaiting release once the
        /// establishing baseline is admitted or discarded if it is not.
        /// </summary>
        public readonly List<StateEventPublication> HeldEvents = [];

        /// <summary>
        /// The most recent Snapshot value received while <see cref="Phase"/> is
        /// <see cref="AreaDeliveryPhase.Recovering"/>, replacing any earlier one rather than
        /// accumulating -- unlike <see cref="HeldEvents"/>, a Snapshot is a complete replacement value,
        /// so only the newest one is ever worth keeping. <see langword="null"/> when no Snapshot has
        /// arrived during the current recovery attempt. Sent once the establishing baseline commits
        /// <see cref="AreaDeliveryPhase.Live"/> if newer than that baseline, or discarded if superseded
        /// by it.
        /// </summary>
        public StateSnapshotPublication? PendingSnapshot;

        /// <summary>
        /// The message id the current, most recently establishing, or currently pending baseline
        /// correlates to -- the originating <c>subscribe</c> or <c>snapshot_request</c>'s own id,
        /// recorded by <see cref="TryEstablishBaseline"/>. Reused, rather than replaced with a fresh
        /// host-generated id, when a held-Event buffer overflow or a newly available authoritative
        /// value re-attempts the same still-pending request. Also meaningful while <see cref="Phase"/>
        /// is <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, unlike every other field on this type:
        /// a non-<see langword="null"/> value there means a request is genuinely pending -- bounded by
        /// <see cref="PendingBaselineDeadlineCancellation"/> -- rather than that no request was ever
        /// made for this area.
        /// </summary>
        public string? RecoveryCorrelationMessageId;

        /// <summary>
        /// The bounded deadline armed for the current <see cref="RecoveryCorrelationMessageId"/> while
        /// no authoritative value has been available to satisfy it, or <see langword="null"/> when no
        /// deadline is currently armed. Cancelled (and cleared) the moment the pending request is
        /// either satisfied, superseded by a fresh attempt, or answered with an explicit timeout
        /// error, so at most one deadline is ever outstanding per area.
        /// </summary>
        public CancellationTokenSource? PendingBaselineDeadlineCancellation;
    }
}
