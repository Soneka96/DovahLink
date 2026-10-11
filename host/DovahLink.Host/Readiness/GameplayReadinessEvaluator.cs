using DovahLink.Host.Adapter;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Readiness;

/// <summary>
/// The Host's single authoritative answer to "is gameplay ready": a coarse ready / not-ready fact
/// derived from the whole predicate, never from a play context alone. Every Host consumer that needs
/// readiness (the public readiness publication, six-digit pairing, and later Stage 5A components)
/// reads it from here rather than evaluating its own copy.
/// </summary>
public interface IGameplayReadinessEvaluator
{
    /// <summary>
    /// Whether gameplay is ready right now: the Adapter is available, its resynchronization is
    /// complete and was armed for the current play-context transition, and a play context is
    /// active. Computed from a validated read on every call, so it is already false the moment an
    /// Adapter disconnect or play-context change is committed -- before any change event is raised.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Raised after readiness actually changed, with the value that was committed. Changes are
    /// raised one at a time in commit order. A subscriber that must act on the newest value reads
    /// <see cref="IsReady"/> rather than trusting a value that may already be superseded.
    /// </summary>
    event Action<bool>? ReadinessChanged;
}

/// <inheritdoc cref="IGameplayReadinessEvaluator"/>
/// <remarks>
/// Adapter availability and the play context live behind two independent locks, and a play-context
/// transition commits before the resynchronization re-arm that follows it. Reading the two
/// separately could therefore report ready for a context that was committed but not yet re-armed.
/// The evaluator closes that window by requiring the Adapter's armed transition generation to equal
/// the play context's current transition generation, and by validating that the play context did not
/// change while the Adapter snapshot was read.
/// </remarks>
public sealed class GameplayReadinessEvaluator : IGameplayReadinessEvaluator
{
    /// <summary>The number of validated-read attempts before the evaluator answers not ready under constant change.</summary>
    private const int MaxReadAttempts = 8;

    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Serializes change detection with ordered change publication; also guards <see cref="lastEvaluated"/>.</summary>
    private readonly object publicationGate = new();

    /// <summary>The value last committed and published; the baseline a change is detected against.</summary>
    private bool lastEvaluated;

    /// <summary>
    /// Creates the evaluator and subscribes it to the authoritative signals for the host process's
    /// own lifetime, matching the other tracker consumers; it is never unsubscribed.
    /// </summary>
    /// <param name="adapterAvailabilityTracker">The Adapter availability, resynchronization, and connection-generation source.</param>
    /// <param name="playContextTracker">The active play-context source.</param>
    public GameplayReadinessEvaluator(
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker)
    {
        ArgumentNullException.ThrowIfNull(adapterAvailabilityTracker);
        ArgumentNullException.ThrowIfNull(playContextTracker);

        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;

        // Subscribe first so no signal committed during construction is lost, then take the
        // baseline under the publication gate so a concurrent re-evaluation cannot interleave.
        adapterAvailabilityTracker.AvailabilityChanged += _ => Reevaluate();
        adapterAvailabilityTracker.Resynchronized += (_, _) => Reevaluate();
        playContextTracker.Transitioned += _ => Reevaluate();
        lock (publicationGate)
        {
            lastEvaluated = Evaluate();
        }
    }

    /// <inheritdoc/>
    public event Action<bool>? ReadinessChanged;

    /// <inheritdoc/>
    public bool IsReady => Evaluate();

    /// <summary>Evaluates the predicate from a validated read of both trackers; not ready if the read cannot be stabilized.</summary>
    private bool Evaluate()
    {
        for (int attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            PlayContextSnapshot before = playContextTracker.GetSnapshot();
            AdapterAvailabilitySnapshot adapter = adapterAvailabilityTracker.GetSnapshot();
            PlayContextSnapshot after = playContextTracker.GetSnapshot();
            if (before.TransitionGeneration != after.TransitionGeneration)
            {
                continue;
            }

            return before.Current is not null
                && adapter.Current == AdapterAvailability.Available
                && !adapter.NeedsResynchronization
                && adapter.ArmedPlayContextTransitionGeneration == before.TransitionGeneration;
        }

        return false;
    }

    /// <summary>Detects a change against the last committed value and publishes it, in commit order, outside any tracker lock.</summary>
    private void Reevaluate()
    {
        lock (publicationGate)
        {
            bool value = Evaluate();
            if (value == lastEvaluated)
            {
                return;
            }

            lastEvaluated = value;

            Delegate[]? subscribers = ReadinessChanged?.GetInvocationList();
            if (subscribers is null)
            {
                return;
            }

            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((Action<bool>)subscriber).Invoke(value);
                }
                catch (Exception)
                {
                    // The change is already committed; one failing subscriber must not suppress
                    // the others or surface into the tracker callback that triggered it.
                }
            }
        }
    }
}
