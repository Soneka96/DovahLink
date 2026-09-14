using DovahLink.Host.Adapter;

namespace DovahLink.Host.Identity;

/// <summary>
/// Owns the Host's <see cref="StateAuthorityId"/> rotation state machine, per
/// <c>plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md</c>
/// Section C's continuity-epoch invariant: the value changes exactly once at the moment an
/// Adapter/IPC continuity loss is detected, stays locked under that value through any number of
/// failed recovery attempts until a fresh authoritative baseline is established, and rotates again
/// only for a loss detected after that baseline exists.
/// </summary>
public interface IStateAuthorityLifecycle
{
    /// <summary>
    /// The current continuity epoch's identity. Never read while <see cref="IsFaulted"/> is
    /// <see langword="true"/> -- a runtime mint failure leaves no value safe to publish.
    /// </summary>
    /// <exception cref="InvalidOperationException">A runtime rotation already failed; see <see cref="IsFaulted"/>.</exception>
    StateAuthorityId Current { get; }

    /// <summary>
    /// Whether a runtime rotation failed after a continuity break was detected. Once
    /// <see langword="true"/>, this never resets: per Section C's failure-behavior policy this is a
    /// fatal Host invariant failure, not a degraded-mode condition, and the Host must proceed to its
    /// normal deterministic shutdown rather than keep serving under any value.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>
    /// Raised exactly once, the moment a runtime rotation fails. Subscribers must stop publishing or
    /// admitting under any <see cref="StateAuthorityId"/> and drive the Host toward shutdown.
    /// </summary>
    event Action? FatalFailureOccurred;

    /// <summary>
    /// Raised the moment a runtime rotation succeeds, carrying the newly minted value. Per Section
    /// C's post-rotation baseline rule, a subscriber holding a live baseline established under the
    /// previous value must invalidate it -- incremental continuity from the previous value is invalid
    /// until a fresh baseline is established under this new one. Never raised for the startup mint,
    /// a harmless resynchronization with no continuity break in progress, or a further loss already
    /// covered by an unresolved break (see <see cref="StateAuthorityLifecycle"/>'s repeated-loss
    /// lock-down); never raised at all once <see cref="IsFaulted"/> becomes <see langword="true"/>.
    /// </summary>
    event Action<StateAuthorityId>? Rotated;
}

/// <inheritdoc cref="IStateAuthorityLifecycle"/>
public sealed class StateAuthorityLifecycle : IStateAuthorityLifecycle
{
    /// <summary>Mints a new underlying identifier value; overridable so a test can force a mint failure.</summary>
    private readonly Func<Guid> idFactory;

    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The current continuity epoch's identity, meaningless once <see cref="isFaulted"/> is set.</summary>
    private StateAuthorityId current;

    /// <summary>
    /// Whether the continuity break that produced <see cref="current"/> is still unresolved -- set
    /// when a loss rotates the value, cleared when a fresh authoritative baseline is established.
    /// While set, a further detected loss is part of the same break and must not rotate again.
    /// </summary>
    private bool insideUnresolvedBreak;

    /// <summary>Whether a runtime rotation has already failed. See <see cref="IsFaulted"/>.</summary>
    private bool isFaulted;

    /// <summary>
    /// Mints the Host-startup value eagerly and subscribes to <paramref name="adapterAvailability"/>'s
    /// loss/recovery signals. A failure minting the startup value propagates out of this constructor
    /// uncaught -- Host composition's own fail-closed startup handling is this type's startup-failure
    /// path, per Section C's "at startup: the Host fails closed" case.
    /// </summary>
    /// <param name="adapterAvailability">The Adapter availability signal this lifecycle rotates against.</param>
    /// <param name="idFactory">Mints a new underlying identifier value. Defaults to <see cref="Guid.NewGuid"/>.</param>
    public StateAuthorityLifecycle(IAdapterAvailabilityTracker adapterAvailability, Func<Guid>? idFactory = null)
    {
        this.idFactory = idFactory ?? Guid.NewGuid;
        current = new StateAuthorityId(this.idFactory());
        adapterAvailability.AvailabilityChanged += HandleAvailabilityChanged;
        adapterAvailability.Resynchronized += HandleResynchronized;
    }

    /// <inheritdoc/>
    public StateAuthorityId Current
    {
        get
        {
            lock (gate)
            {
                if (isFaulted)
                {
                    throw new InvalidOperationException("A runtime state-authority rotation already failed; no value is safe to publish.");
                }

                return current;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsFaulted
    {
        get
        {
            lock (gate)
            {
                return isFaulted;
            }
        }
    }

    /// <inheritdoc/>
    public event Action? FatalFailureOccurred;

    /// <inheritdoc/>
    public event Action<StateAuthorityId>? Rotated;

    /// <summary>
    /// Rotates on a newly detected continuity loss, per Section C's core invariant. Ignores a
    /// recovery transition and a further loss already covered by an unresolved break.
    /// </summary>
    /// <param name="transition">The committed availability transition.</param>
    private void HandleAvailabilityChanged(AdapterAvailabilityTransition transition)
    {
        if (transition.Current != AdapterAvailability.Unavailable)
        {
            return;
        }

        StateAuthorityId? rotatedTo = null;
        bool shouldRaiseFatalFailure = false;
        lock (gate)
        {
            if (isFaulted || insideUnresolvedBreak)
            {
                return;
            }

            try
            {
                current = new StateAuthorityId(idFactory());
                insideUnresolvedBreak = true;
                rotatedTo = current;
            }
            catch
            {
                isFaulted = true;
                shouldRaiseFatalFailure = true;
            }
        }

        if (rotatedTo is StateAuthorityId newValue)
        {
            RaiseRotated(newValue);
        }

        if (shouldRaiseFatalFailure)
        {
            RaiseFatalFailure();
        }
    }

    /// <summary>
    /// Invokes every <see cref="FatalFailureOccurred"/> subscriber, containing each one's own
    /// exception individually -- the same isolation <see cref="AdapterAvailabilityTracker.PublishTransition"/>
    /// already documents for <see cref="IAdapterAvailabilityTracker.AvailabilityChanged"/>, so one
    /// failing subscriber can never suppress this fatal signal from reaching the rest.
    /// </summary>
    private void RaiseFatalFailure()
    {
        Delegate[]? subscribers = FatalFailureOccurred?.GetInvocationList();
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception)
            {
                // A subscriber's own failure must never prevent another subscriber from learning
                // that the Host must shut down.
            }
        }
    }

    /// <summary>
    /// Invokes every <see cref="Rotated"/> subscriber with <paramref name="newValue"/>, containing
    /// each one's own exception individually -- the same isolation <see cref="RaiseFatalFailure"/>
    /// already applies, so one failing subscriber can never suppress this signal from reaching the
    /// rest.
    /// </summary>
    /// <param name="newValue">The newly minted value this rotation produced.</param>
    private void RaiseRotated(StateAuthorityId newValue)
    {
        Delegate[]? subscribers = Rotated?.GetInvocationList();
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<StateAuthorityId>)subscriber).Invoke(newValue);
            }
            catch (Exception)
            {
                // A subscriber's own failure must never prevent another subscriber from learning
                // that the value rotated.
            }
        }
    }

    /// <summary>
    /// Clears the unresolved-break lock once a fresh authoritative baseline is established.
    /// <paramref name="instanceId"/>/<paramref name="connectionGeneration"/> are not re-validated
    /// here: <see cref="IAdapterAvailabilityTracker.Resynchronized"/>'s own contract guarantees it
    /// only fires for a resynchronization the tracker itself already matched against its current
    /// connection.
    /// </summary>
    /// <param name="instanceId">The adapter instance that completed resynchronization.</param>
    /// <param name="connectionGeneration">The connection generation that resynchronized.</param>
    private void HandleResynchronized(AdapterInstanceId instanceId, long connectionGeneration)
    {
        lock (gate)
        {
            insideUnresolvedBreak = false;
        }
    }
}
