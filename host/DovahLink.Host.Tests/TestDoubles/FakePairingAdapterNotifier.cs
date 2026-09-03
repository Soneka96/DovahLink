using DovahLink.Host.Client.Dispatch;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A configurable in-memory stand-in for <see cref="IPairingAdapterNotifier"/>.</summary>
public sealed class FakePairingAdapterNotifier : IPairingAdapterNotifier
{
    /// <summary>Whether <see cref="TryNotifyCodeAvailableAsync"/> reports acceptance.</summary>
    public bool AcceptDisplay { get; set; } = true;

    /// <summary>Whether <see cref="TryNotifyRedisplayAsync"/> reports acceptance.</summary>
    public bool AcceptRedisplay { get; set; } = true;

    /// <summary>Every code passed to <see cref="TryNotifyCodeAvailableAsync"/>, in call order.</summary>
    public List<string> DisplayedCodes { get; } = [];

    /// <summary>Every code passed to <see cref="TryNotifyRedisplayAsync"/>, in call order.</summary>
    public List<string> RedisplayedCodes { get; } = [];

    /// <summary>Every code passed to <see cref="NotifyCodeIncorrectAsync"/>, in call order.</summary>
    public List<string> IncorrectCodeNotifications { get; } = [];

    /// <summary>The number of times <see cref="NotifyAttemptsExhaustedAsync"/> has been called.</summary>
    public int AttemptsExhaustedCallCount { get; private set; }

    /// <inheritdoc/>
    public Task<bool> TryNotifyCodeAvailableAsync(string code, CancellationToken cancellationToken)
    {
        DisplayedCodes.Add(code);
        return Task.FromResult(AcceptDisplay);
    }

    /// <inheritdoc/>
    public Task<bool> TryNotifyRedisplayAsync(string code, CancellationToken cancellationToken)
    {
        RedisplayedCodes.Add(code);
        return Task.FromResult(AcceptRedisplay);
    }

    /// <inheritdoc/>
    public Task NotifyCodeIncorrectAsync(string code, CancellationToken cancellationToken)
    {
        IncorrectCodeNotifications.Add(code);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task NotifyAttemptsExhaustedAsync(CancellationToken cancellationToken)
    {
        AttemptsExhaustedCallCount++;
        return Task.CompletedTask;
    }
}
