using DovahLink.Host.Readiness;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable in-memory stand-in for <see cref="IGameplayReadinessEvaluator"/>.</summary>
public sealed class FakeGameplayReadinessEvaluator : IGameplayReadinessEvaluator
{
    /// <inheritdoc/>
    public bool IsReady { get; set; }

    /// <inheritdoc/>
    public event Action<bool>? ReadinessChanged;

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
}
