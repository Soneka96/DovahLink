using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Readiness;

namespace DovahLink.Host.Client.Readiness;

/// <summary>
/// One admitted session's publisher of the Host's coarse gameplay-readiness fact. It owns only the
/// per-session wire publication -- sequence, ordering, teardown, and send-failure policy; the
/// readiness fact itself always comes from the single <see cref="IGameplayReadinessEvaluator"/>.
/// It is session-scoped and works for restricted and trusted sessions alike: readiness is neither a
/// state area nor a trust privilege.
/// </summary>
public interface IPublicGameplayReadinessPublisher
{
    /// <summary>
    /// Binds this publisher to the connection and session admission just established and publishes
    /// the current value once, as the first readiness message of the session. Must be called after
    /// <c>hello_ack</c> has been sent so readiness follows it on the wire. Subsequent calls, and any
    /// call after <see cref="Unbind"/>, do nothing.
    /// </summary>
    /// <param name="connectionContext">The connection to publish through.</param>
    /// <param name="sessionId">The admitted session identity stamped onto every message.</param>
    void Bind(IPublicConnectionContext connectionContext, SessionId sessionId);

    /// <summary>
    /// Stops publishing for good: this session can never publish again, including from a callback
    /// that was already in flight. Idempotent; safe if <see cref="Bind"/> was never called.
    /// </summary>
    void Unbind();
}

/// <inheritdoc cref="IPublicGameplayReadinessPublisher"/>
/// <remarks>
/// <para>
/// Ordering and sequence: the sequence is assigned and the message handed to the connection's
/// bounded control lane under one gate, so sequence order is wire order. It starts at 1 per
/// publisher instance, and the host builds one instance per connection, so it is never shared
/// between sessions.
/// </para>
/// <para>
/// Value and provenance: on every trigger the publisher re-reads the evaluator's current value
/// instead of trusting the value carried by the change event, so an obsolete value is never sent
/// after a newer one is already established. The authority identity is captured before the value is
/// read and re-validated after, so a value is never stamped with an authority it was not read under.
/// A message is published only when the (value, authority) pair differs from the last one sent: a
/// repeated equivalent fact produces no new sequence, while an authority rotation with an unchanged
/// value still produces a fresh message, since the value a client held belongs to the old epoch.
/// </para>
/// <para>
/// Failure: a control-lane admission failure closes the connection through the existing
/// <see cref="IPublicConnectionContext.RequestClose"/> policy. The session is never left believing
/// an obsolete value is current; a reconnect receives a fresh initial value.
/// </para>
/// </remarks>
public sealed class PublicGameplayReadinessPublisher : IPublicGameplayReadinessPublisher
{
    /// <summary>The number of authority-validated read attempts before the publisher fails the connection closed.</summary>
    private const int MaxReadAttempts = 8;

    private readonly IGameplayReadinessEvaluator evaluator;

    private readonly IPublicEnvelopeCodec codec;

    private readonly IPlayContextTracker playContextTracker;

    private readonly IStateAuthorityLifecycle stateAuthorityLifecycle;

    /// <summary>Guards every field below and serializes sequence assignment with the send.</summary>
    private readonly object gate = new();

    private IPublicConnectionContext? connectionContext;

    private SessionId sessionId;

    private bool bound;

    private bool ended;

    /// <summary>The sequence of the last message handed to the connection; zero before the first.</summary>
    private long sequence;

    /// <summary>The (value, authority) pair last published, or <see langword="null"/> before the first publication.</summary>
    private (bool Ready, StateAuthorityId Authority)? lastPublished;

    /// <summary>Creates a publisher for one connection.</summary>
    /// <param name="evaluator">The Host's single gameplay-readiness authority.</param>
    /// <param name="codec">Encodes the readiness message.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto the envelope.</param>
    /// <param name="stateAuthorityLifecycle">Supplies the authority epoch each value is published under.</param>
    public PublicGameplayReadinessPublisher(
        IGameplayReadinessEvaluator evaluator,
        IPublicEnvelopeCodec codec,
        IPlayContextTracker playContextTracker,
        IStateAuthorityLifecycle stateAuthorityLifecycle)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(playContextTracker);
        ArgumentNullException.ThrowIfNull(stateAuthorityLifecycle);

        this.evaluator = evaluator;
        this.codec = codec;
        this.playContextTracker = playContextTracker;
        this.stateAuthorityLifecycle = stateAuthorityLifecycle;
    }

    /// <inheritdoc/>
    public void Bind(IPublicConnectionContext connectionContext, SessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(connectionContext);

        lock (gate)
        {
            if (bound || ended)
            {
                return;
            }

            bound = true;
            this.connectionContext = connectionContext;
            this.sessionId = sessionId;
            evaluator.ReadinessChanged += OnSignal;
            stateAuthorityLifecycle.Rotated += OnRotated;
            PublishCurrentLocked();
        }
    }

    /// <inheritdoc/>
    public void Unbind()
    {
        lock (gate)
        {
            EndLocked();
        }
    }

    private void OnSignal(bool _) => PublishIfCurrent();

    private void OnRotated(StateAuthorityId _) => PublishIfCurrent();

    private void PublishIfCurrent()
    {
        lock (gate)
        {
            if (bound && !ended)
            {
                PublishCurrentLocked();
            }
        }
    }

    /// <summary>Publishes the current (value, authority) pair if it differs from the last one sent. Caller holds <see cref="gate"/>.</summary>
    private void PublishCurrentLocked()
    {
        for (int attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            StateAuthorityId authority;
            try
            {
                authority = stateAuthorityLifecycle.Current;
            }
            catch (InvalidOperationException)
            {
                // A faulted lifecycle has no safe authority to stamp; fail the connection closed.
                FailClosedLocked();
                return;
            }

            bool ready = evaluator.IsReady;
            if (!IsStillCurrent(authority))
            {
                continue;
            }

            if (lastPublished is { } previous && previous.Ready == ready && previous.Authority == authority)
            {
                return;
            }

            long nextSequence = sequence + 1;
            byte[] bytes = codec.EncodeStatePublication(
                PublicMessageType.GameplayReadiness,
                Guid.NewGuid().ToString(),
                sessionId.ToString(),
                null,
                playContextTracker.GetSnapshot().Current?.ToString(),
                null,
                authority,
                new GameplayReadinessPayload { Ready = ready, Sequence = nextSequence });

            if (!connectionContext!.TrySend(bytes, PublicOutboundLane.ControlOrRecovery))
            {
                FailClosedLocked();
                return;
            }

            sequence = nextSequence;
            lastPublished = (ready, authority);
            return;
        }

        FailClosedLocked();
    }

    private bool IsStillCurrent(StateAuthorityId authority)
    {
        try
        {
            return stateAuthorityLifecycle.Current == authority;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Ends this publisher and closes the connection through the existing failure policy. Caller holds <see cref="gate"/>.</summary>
    private void FailClosedLocked()
    {
        IPublicConnectionContext? context = connectionContext;
        EndLocked();
        context?.RequestClose();
    }

    /// <summary>Marks the publisher ended and detaches from the shared signals. Caller holds <see cref="gate"/>.</summary>
    private void EndLocked()
    {
        if (ended)
        {
            return;
        }

        ended = true;
        connectionContext = null;
        if (bound)
        {
            evaluator.ReadinessChanged -= OnSignal;
            stateAuthorityLifecycle.Rotated -= OnRotated;
        }
    }
}
