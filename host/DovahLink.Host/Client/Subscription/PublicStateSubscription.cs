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
/// <c>protocol/schema/README.md</c>'s "the bridge sends a snapshot before events for each accepted
/// state area." A play-context transition invalidates every area's live baseline and any Events held
/// for it, so a later Event stops forwarding until this connection obtains a fresh baseline.
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
    /// value available yet is skipped, never fabricated.
    /// </summary>
    /// <param name="acceptedStateAreas">The areas <see cref="HandleSubscribe"/> just reported accepted.</param>
    /// <param name="correlationMessageId">The originating <c>subscribe</c> message's own id.</param>
    void EstablishAcceptedBaselines(IReadOnlyList<string> acceptedStateAreas, string correlationMessageId);

    /// <summary>
    /// Answers a <c>snapshot_request</c>: sends a fresh baseline for <paramref name="stateArea"/>,
    /// correlated to <paramref name="correlationMessageId"/>, when it is registered and a current
    /// value is available, arming that area's event-forwarding gate only once the connection actually
    /// admits the baseline -- the same successful-admission requirement <see cref="HandleSubscribe"/>
    /// applies. Sends nothing when the area is registered but no value is available yet -- that value
    /// is deferred, never fabricated.
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

    /// <summary>Creates a subscription bound to no connection yet; call <see cref="Bind"/> once admission completes.</summary>
    /// <param name="registeredStateAreaPolicy">The host-wide bounded set of state areas currently served.</param>
    /// <param name="feed">The host-wide, domain-agnostic push source snapshots and events are read from.</param>
    /// <param name="codec">Encodes every message this subscription sends.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every message this subscription sends.</param>
    public PublicStateSubscription(
        IRegisteredStateAreaPolicy registeredStateAreaPolicy,
        IStatePublicationFeed feed,
        IPublicEnvelopeCodec codec,
        IPlayContextTracker playContextTracker)
    {
        this.registeredStateAreaPolicy = registeredStateAreaPolicy;
        this.feed = feed;
        this.codec = codec;
        this.playContextTracker = playContextTracker;
        feed.EventOccurred += OnEventOccurred;
        playContextTracker.Transitioned += OnPlayContextTransitioned;
    }

    /// <inheritdoc/>
    public void Bind(IPublicConnectionContext connectionContext, SessionId sessionId)
    {
        lock (gate)
        {
            this.connectionContext = connectionContext;
            this.sessionId = sessionId;
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
        List<(StateAreaId AreaId, StateSnapshotPublication Snapshot)> baselinesToSend = [];

        lock (gate)
        {
            foreach (string accepted in acceptedStateAreas)
            {
                var areaId = new StateAreaId(accepted);
                bool needsBaseline = !areaStates.TryGetValue(areaId, out AreaState? state) || state.Phase != AreaDeliveryPhase.Live;
                if (needsBaseline && feed.TryGetSnapshot(areaId, out StateSnapshotPublication? snapshot))
                {
                    baselinesToSend.Add((areaId, snapshot));
                }
            }
        }

        foreach ((StateAreaId areaId, StateSnapshotPublication snapshot) in baselinesToSend)
        {
            TryEstablishBaseline(areaId, snapshot, correlationMessageId);
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

        if (feed.TryGetSnapshot(areaId, out StateSnapshotPublication? snapshot))
        {
            TryEstablishBaseline(areaId, snapshot, correlationMessageId);
        }

        return true;
    }

    /// <inheritdoc/>
    public void Unsubscribe()
    {
        feed.EventOccurred -= OnEventOccurred;
        playContextTracker.Transitioned -= OnPlayContextTransitioned;
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
            foreach (AreaState state in areaStates.Values)
            {
                state.Phase = AreaDeliveryPhase.AwaitingBaseline;
                state.HeldEvents.Clear();
                state.RecoveryEpoch++;
            }
        }
    }

    /// <summary>
    /// Routes <paramref name="eventPublication"/> for this connection according to its state area's
    /// current recovery-barrier phase: discarded while <see cref="AreaDeliveryPhase.AwaitingBaseline"/>;
    /// while <see cref="AreaDeliveryPhase.Recovering"/>, discarded if superseded by the barrier
    /// revision, otherwise held until the baseline resolves (abandoning the held set and re-baselining
    /// from the newest authoritative snapshot if the bounded hold fills up); forwarded immediately
    /// while <see cref="AreaDeliveryPhase.Live"/>. Also discarded outright, regardless of phase, when
    /// its own captured play-context generation does not match the tracker's current one -- a stale
    /// value from before a transition this subscription has not yet been told to forward.
    /// </summary>
    /// <param name="eventPublication">The event the feed just published.</param>
    private void OnEventOccurred(StateEventPublication eventPublication)
    {
        StateEventPublication? eventToForward = null;
        bool needsReBaseline = false;

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
                    if (eventPublication.Revision.Value <= state.BarrierRevision.Value)
                    {
                        break;
                    }

                    if (state.HeldEvents.Count >= Constants.MaxHeldRecoveryEventsPerArea)
                    {
                        state.HeldEvents.Clear();
                        state.RecoveryEpoch++;
                        state.Phase = AreaDeliveryPhase.AwaitingBaseline;
                        needsReBaseline = true;
                    }
                    else
                    {
                        state.HeldEvents.Add(eventPublication);
                    }

                    break;

                case AreaDeliveryPhase.Live:
                    if (eventPublication.PlayContextGeneration == state.PlayContextGeneration)
                    {
                        eventToForward = eventPublication;
                    }

                    break;
            }
        }

        if (eventToForward is not null)
        {
            ForwardEvent(eventToForward);
        }
        else if (needsReBaseline && feed.TryGetSnapshot(eventPublication.StateArea, out StateSnapshotPublication? freshSnapshot))
        {
            TryEstablishBaseline(eventPublication.StateArea, freshSnapshot, correlationMessageId: NewMessageId());
        }
    }

    /// <summary>
    /// Establishes <paramref name="snapshot"/> as <paramref name="areaId"/>'s new baseline through the
    /// reserved Control/Recovery lane. Enters <see cref="AreaDeliveryPhase.Recovering"/> under a fresh
    /// recovery epoch before sending, so a concurrent Event above the snapshot's own revision is held
    /// by <see cref="OnEventOccurred"/> rather than discarded or forwarded ahead of the baseline that
    /// establishes it. Commits <see cref="AreaDeliveryPhase.Live"/> and releases the held Events, in
    /// arrival order, only once the connection actually admits the baseline and this remains the most
    /// recent recovery attempt for the area -- a later attempt, or a play-context transition, bumps
    /// the area's recovery epoch and makes this call's eventual result a no-op when it completes. Falls
    /// back to <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, discarding any held Events, when
    /// admission fails. Makes no change at all when this subscription is not currently bound to a
    /// connection, or when <paramref name="snapshot"/>'s own captured play-context generation is
    /// already stale relative to the tracker's current one -- treated the same as no value being
    /// available yet, never sent under a label it was not actually captured under.
    /// </summary>
    /// <param name="areaId">The state area this snapshot belongs to.</param>
    /// <param name="snapshot">The value to establish as the new baseline.</param>
    /// <param name="correlationMessageId">The message id this snapshot correlates to.</param>
    private void TryEstablishBaseline(StateAreaId areaId, StateSnapshotPublication snapshot, string correlationMessageId)
    {
        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        long myEpoch;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
            if (snapshot.PlayContextGeneration != playContextTracker.GetSnapshot().TransitionGeneration)
            {
                return;
            }

            AreaState state = GetOrCreateAreaState(areaId);
            state.Phase = AreaDeliveryPhase.Recovering;
            state.BarrierRevision = snapshot.Revision;
            state.PlayContextGeneration = snapshot.PlayContextGeneration;
            state.HeldEvents.Clear();
            myEpoch = ++state.RecoveryEpoch;
        }

        if (currentConnectionContext is null || currentSessionId is null)
        {
            return;
        }

        var payload = new StateSnapshotPayload
        {
            StateArea = areaId.Value,
            Revision = snapshot.Revision.Value,
            OccurredAt = snapshot.OccurredAt,
            Data = snapshot.Data,
        };
        byte[] bytes = codec.Encode(
            PublicMessageType.StateSnapshot,
            NewMessageId(),
            currentSessionId.Value.ToString(),
            correlationMessageId,
            snapshot.PlayContextId?.ToString(),
            null,
            payload);

        bool admitted = currentConnectionContext.TrySend(bytes, PublicOutboundLane.ControlOrRecovery);

        List<StateEventPublication> releasedEvents = [];
        lock (gate)
        {
            if (!areaStates.TryGetValue(areaId, out AreaState? state) || state.RecoveryEpoch != myEpoch)
            {
                return;
            }

            if (admitted)
            {
                state.Phase = AreaDeliveryPhase.Live;
                releasedEvents.AddRange(state.HeldEvents);
                state.HeldEvents.Clear();
            }
            else
            {
                state.Phase = AreaDeliveryPhase.AwaitingBaseline;
                state.HeldEvents.Clear();
            }
        }

        foreach (StateEventPublication heldEvent in releasedEvents)
        {
            ForwardEvent(heldEvent);
        }
    }

    /// <summary>Encodes and sends one already-accepted, already-live Event on the Data lane, labeled with its own captured play context.</summary>
    /// <param name="eventPublication">The event to forward.</param>
    private void ForwardEvent(StateEventPublication eventPublication)
    {
        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
        }

        if (currentConnectionContext is null || currentSessionId is null)
        {
            return;
        }

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
            currentSessionId.Value.ToString(),
            null,
            eventPublication.PlayContextId?.ToString(),
            null,
            payload);
        currentConnectionContext.TrySend(bytes, PublicOutboundLane.Data);
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
    /// token identifying the most recent recovery attempt, and any Event held above the barrier
    /// revision while <see cref="AreaDeliveryPhase.Recovering"/>. All access is guarded by the owning
    /// subscription's own <see cref="gate"/>; this type performs no synchronization of its own.
    /// </summary>
    private sealed class AreaState
    {
        /// <summary>This area's current delivery phase.</summary>
        public AreaDeliveryPhase Phase = AreaDeliveryPhase.AwaitingBaseline;

        /// <summary>
        /// The current or most recently established baseline's revision. Meaningful only while
        /// <see cref="Phase"/> is <see cref="AreaDeliveryPhase.Recovering"/> or
        /// <see cref="AreaDeliveryPhase.Live"/>.
        /// </summary>
        public RevisionNumber BarrierRevision;

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
    }
}
