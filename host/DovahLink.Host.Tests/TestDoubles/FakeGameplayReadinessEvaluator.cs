using DovahLink.Host.Identity;
using DovahLink.Host.Readiness;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable in-memory stand-in for <see cref="IGameplayReadinessEvaluator"/>.</summary>
public sealed class FakeGameplayReadinessEvaluator : IGameplayReadinessEvaluator
{
    /// <inheritdoc/>
    public bool IsReady { get; set; }

    /// <summary>The play context <see cref="GetSnapshot"/> reports; <see langword="null"/> until a test sets one.</summary>
    public PlayContextId? PlayContext { get; set; }

    /// <summary>The play-context transition generation <see cref="GetSnapshot"/> reports.</summary>
    public long PlayContextTransitionGeneration { get; set; }

    /// <summary>The Adapter instance <see cref="GetSnapshot"/> reports; <see langword="null"/> until a test sets one.</summary>
    public AdapterInstanceId? AdapterInstanceId { get; set; }

    /// <summary>The Adapter connection generation <see cref="GetSnapshot"/> reports.</summary>
    public long ConnectionGeneration { get; set; }

    /// <summary>
    /// Optional work run once <see cref="GetSnapshot"/> has built its result and before it returns, so
    /// a test can land a state change in the window right after a read -- the interleaving a consumer
    /// that reads the value and its context separately would pair incorrectly.
    /// </summary>
    public Action? AfterSnapshotRead { get; set; }

    /// <inheritdoc/>
    public event Action<bool>? ReadinessChanged;

    /// <summary>Whether any subscriber is currently attached to <see cref="ReadinessChanged"/>.</summary>
    public bool HasSubscribers => ReadinessChanged is not null;

    /// <inheritdoc/>
    public GameplayReadinessSnapshot GetSnapshot()
    {
        var snapshot = new GameplayReadinessSnapshot(
            IsReady, PlayContext, PlayContextTransitionGeneration, AdapterInstanceId, ConnectionGeneration);
        AfterSnapshotRead?.Invoke();
        return snapshot;
    }

    /// <summary>Sets <see cref="IsReady"/> and raises <see cref="ReadinessChanged"/> when the value actually changed.</summary>
    /// <param name="ready">The new readiness value.</param>
    public void SetReady(bool ready)
    {
        if (IsReady == ready)
        {
            return;
        }

        IsReady = ready;
        ReadinessChanged?.Invoke(ready);
    }

    /// <summary>
    /// Raises <see cref="ReadinessChanged"/> carrying <paramref name="reportedValue"/> without
    /// touching <see cref="IsReady"/>, so a test can model a duplicate or already-superseded change
    /// notification.
    /// </summary>
    /// <param name="reportedValue">The value the notification carries.</param>
    public void RaiseReadinessChanged(bool reportedValue) => ReadinessChanged?.Invoke(reportedValue);
}
