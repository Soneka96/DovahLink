using DovahLink.Host.Readiness;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable in-memory stand-in for <see cref="IGameplayReadinessEvaluator"/>.</summary>
public sealed class FakeGameplayReadinessEvaluator : IGameplayReadinessEvaluator
{
    /// <inheritdoc/>
    public bool IsReady { get; set; }

    /// <inheritdoc/>
    public event Action<bool>? ReadinessChanged;

    /// <summary>Whether any subscriber is currently attached to <see cref="ReadinessChanged"/>.</summary>
    public bool HasSubscribers => ReadinessChanged is not null;

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
