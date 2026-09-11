using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

namespace DovahLink.Host.Client.Subscription;

/// <summary>
/// One connection's own subscribed state areas and event-forwarding gate. Created fresh per
/// connection, alongside <see cref="Authentication.PublicHelloAdmissionHandler"/>, so a reconnect
/// never inherits a previous connection's subscriptions or delivery state. Answers <c>subscribe</c>
/// and <c>snapshot_request</c> against the host-wide <see cref="IRegisteredStateAreaPolicy"/> and
/// <see cref="IStatePublicationFeed"/>, and forwards an accepted area's later
/// <see cref="IStatePublicationFeed.EventOccurred"/> events only once this connection has actually
/// had a baseline snapshot admitted for that area under the play context currently active -- per
/// <c>protocol/schema/README.md</c>'s "the bridge sends a snapshot before events for each accepted
/// state area." A play-context transition invalidates every area's live baseline, so a later Event
/// stops forwarding until this connection obtains a fresh one.
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
    /// Every state area this connection currently has a live baseline for, mapped to the play-context
    /// transition generation that baseline was established under. Absence means the area has no live
    /// baseline -- either it never received one, or a play-context transition invalidated it -- and
    /// gates <see cref="OnEventOccurred"/> from forwarding.
    /// </summary>
    private readonly Dictionary<StateAreaId, long> liveGenerationByArea = [];

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

                bool needsBaseline = !liveGenerationByArea.ContainsKey(areaId);
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
                if (!liveGenerationByArea.ContainsKey(areaId) && feed.TryGetSnapshot(areaId, out StateSnapshotPublication? snapshot))
                {
                    baselinesToSend.Add((areaId, snapshot));
                }
            }
        }

        foreach ((StateAreaId areaId, StateSnapshotPublication snapshot) in baselinesToSend)
        {
            SendBaseline(areaId, snapshot, correlationMessageId);
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
            SendBaseline(areaId, snapshot, correlationMessageId);
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
    /// Invalidates every area's live baseline: a baseline established under one play context must
    /// never be treated as current once the context changes, so this connection must obtain a fresh
    /// one -- via <see cref="HandleSnapshotRequest"/>, or a later <see cref="HandleSubscribe"/> for
    /// the same area -- before it forwards another Event for that area. Leaves
    /// <see cref="acceptedAreas"/> untouched; an already-accepted area does not need to be
    /// re-subscribed.
    /// </summary>
    /// <param name="transition">The transition the tracker just committed.</param>
    private void OnPlayContextTransitioned(PlayContextTransition transition)
    {
        lock (gate)
        {
            liveGenerationByArea.Clear();
        }
    }

    /// <summary>
    /// Forwards <paramref name="eventPublication"/> to this connection when its state area is both
    /// accepted and currently has a live baseline under the play context active right now; otherwise
    /// silently ignores it.
    /// </summary>
    /// <param name="eventPublication">The event the feed just published.</param>
    private void OnEventOccurred(StateEventPublication eventPublication)
    {
        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        PlayContextSnapshot playContextSnapshot;
        bool shouldForward;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
            playContextSnapshot = playContextTracker.GetSnapshot();
            shouldForward = acceptedAreas.Contains(eventPublication.StateArea)
                && liveGenerationByArea.TryGetValue(eventPublication.StateArea, out long liveGeneration)
                && liveGeneration == playContextSnapshot.TransitionGeneration;
        }

        if (!shouldForward || currentConnectionContext is null || currentSessionId is null)
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
            playContextSnapshot.Current?.ToString(),
            null,
            payload);
        currentConnectionContext.TrySend(bytes, PublicOutboundLane.Data);
    }

    /// <summary>
    /// Encodes and sends one snapshot as this area's authoritative baseline through the reserved
    /// Control/Recovery lane, marking the area live under the play-context generation it was captured
    /// against only once the connection actually admits it -- never before, so
    /// <see cref="OnEventOccurred"/> can never forward an Event for an area whose baseline the client
    /// never received. Makes no change when this subscription is not currently bound to a connection,
    /// when the connection declines to admit the baseline, or when a play-context transition has
    /// already superseded the generation this baseline was captured against by the time admission
    /// succeeds.
    /// </summary>
    /// <param name="areaId">The state area this snapshot belongs to.</param>
    /// <param name="snapshot">The value to send.</param>
    /// <param name="correlationMessageId">The message id this snapshot correlates to.</param>
    private void SendBaseline(StateAreaId areaId, StateSnapshotPublication snapshot, string correlationMessageId)
    {
        IPublicConnectionContext? currentConnectionContext;
        SessionId? currentSessionId;
        PlayContextSnapshot playContextSnapshot;
        lock (gate)
        {
            currentConnectionContext = connectionContext;
            currentSessionId = sessionId;
            playContextSnapshot = playContextTracker.GetSnapshot();
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
            playContextSnapshot.Current?.ToString(),
            null,
            payload);

        if (!currentConnectionContext.TrySend(bytes, PublicOutboundLane.ControlOrRecovery))
        {
            return;
        }

        lock (gate)
        {
            if (playContextTracker.GetSnapshot().TransitionGeneration == playContextSnapshot.TransitionGeneration)
            {
                liveGenerationByArea[areaId] = playContextSnapshot.TransitionGeneration;
            }
        }
    }

    /// <summary>Generates a fresh, cryptographically random host-originated message identifier.</summary>
    private static string NewMessageId() => Guid.NewGuid().ToString();
}
