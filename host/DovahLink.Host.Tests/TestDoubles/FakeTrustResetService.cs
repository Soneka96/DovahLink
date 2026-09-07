using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="ITrustResetService"/>.</summary>
public sealed class FakeTrustResetService : ITrustResetService
{
    /// <summary>The result <see cref="BeginReset"/> returns.</summary>
    public FactoryResetBeginResult BeginResetResult { get; set; } =
        new(FactoryResetBeginOutcome.Started, new FactoryResetChallenge("123456", DateTimeOffset.UtcNow.AddSeconds(60)));

    /// <summary>The result <see cref="ConfirmResetAsync"/> returns.</summary>
    public bool ConfirmResetResult { get; set; } = true;

    /// <summary>The codes passed to <see cref="ConfirmResetAsync"/>, in call order.</summary>
    public List<string> ConfirmResetCalls { get; } = [];

    /// <inheritdoc/>
    public FactoryResetBeginResult BeginReset() => BeginResetResult;

    /// <inheritdoc/>
    public Task<bool> ConfirmResetAsync(string code, CancellationToken cancellationToken = default)
    {
        ConfirmResetCalls.Add(code);
        return Task.FromResult(ConfirmResetResult);
    }
}
