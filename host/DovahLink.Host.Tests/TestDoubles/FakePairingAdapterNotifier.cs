using DovahLink.Host.Client.Dispatch;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A configurable in-memory stand-in for <see cref="IPairingAdapterNotifier"/>.</summary>
public sealed class FakePairingAdapterNotifier : IPairingAdapterNotifier
{
    /// <summary>Whether <see cref="TryNotifyCodeAvailableAsync"/> reports acceptance.</summary>
    public bool AcceptDisplay { get; set; } = true;

    /// <summary>When set, <see cref="TryNotifyCodeAvailableAsync"/> throws this instead of resolving.</summary>
    public Exception? ThrowOnNotifyCodeAvailable { get; set; }

    /// <summary>Whether <see cref="TryNotifyRedisplayAsync"/> reports acceptance.</summary>
    public bool AcceptRedisplay { get; set; } = true;

    /// <summary>When set, <see cref="TryNotifyRedisplayAsync"/> throws this instead of resolving.</summary>
    public Exception? ThrowOnNotifyRedisplay { get; set; }

    /// <summary>Every code passed to <see cref="TryNotifyCodeAvailableAsync"/>, in call order.</summary>
    public List<string> DisplayedCodes { get; } = [];

    /// <summary>Every code passed to <see cref="TryNotifyRedisplayAsync"/>, in call order.</summary>
    public List<string> RedisplayedCodes { get; } = [];

    /// <summary>Every code passed to <see cref="NotifyCodeIncorrectAsync"/>, in call order.</summary>
    public List<string> IncorrectCodeNotifications { get; } = [];

    /// <summary>The number of times <see cref="NotifyAttemptsExhaustedAsync"/> has been called.</summary>
    public int AttemptsExhaustedCallCount { get; private set; }

    /// <summary>When set, <see cref="NotifyCodeIncorrectAsync"/> returns a faulted task carrying this exception instead of completing.</summary>
    public Exception? ThrowOnNotifyCodeIncorrect { get; set; }

    /// <summary>When set, <see cref="NotifyAttemptsExhaustedAsync"/> returns a faulted task carrying this exception instead of completing.</summary>
    public Exception? ThrowOnNotifyAttemptsExhausted { get; set; }

    /// <summary>When set, <see cref="NotifyCodeIncorrectAsync"/> throws this synchronously before ever returning a <see cref="Task"/>, rather than returning a faulted one.</summary>
    public Exception? ThrowSynchronouslyOnNotifyCodeIncorrect { get; set; }

    /// <summary>When set, <see cref="NotifyAttemptsExhaustedAsync"/> throws this synchronously before ever returning a <see cref="Task"/>, rather than returning a faulted one.</summary>
    public Exception? ThrowSynchronouslyOnNotifyAttemptsExhausted { get; set; }

    /// <summary>
    /// Optional asynchronous work awaited before <see cref="TryNotifyCodeAvailableAsync"/> resolves,
    /// after the code is already recorded in <see cref="DisplayedCodes"/>. Lets a test pause the
    /// caller's await and deterministically interleave a coordinator state change -- cancellation,
    /// replacement, administrative invalidation -- before the acknowledgement is observed.
    /// </summary>
    public Func<Task>? BeforeNotifyCodeAvailable { get; set; }

    /// <summary>
    /// Optional asynchronous work awaited before <see cref="TryNotifyRedisplayAsync"/> resolves, the
    /// same way <see cref="BeforeNotifyCodeAvailable"/> gates the initial display.
    /// </summary>
    public Func<Task>? BeforeNotifyRedisplay { get; set; }

    /// <inheritdoc/>
    public async Task<bool> TryNotifyCodeAvailableAsync(string code, CancellationToken cancellationToken)
    {
        DisplayedCodes.Add(code);
        if (BeforeNotifyCodeAvailable is { } beforeNotify)
        {
            await beforeNotify();
        }

        if (ThrowOnNotifyCodeAvailable is { } exception)
        {
            throw exception;
        }

        return AcceptDisplay;
    }

    /// <inheritdoc/>
    public async Task<bool> TryNotifyRedisplayAsync(string code, CancellationToken cancellationToken)
    {
        RedisplayedCodes.Add(code);
        if (BeforeNotifyRedisplay is { } beforeNotify)
        {
            await beforeNotify();
        }

        if (ThrowOnNotifyRedisplay is { } exception)
        {
            throw exception;
        }

        return AcceptRedisplay;
    }

    /// <inheritdoc/>
    public Task NotifyCodeIncorrectAsync(string code, CancellationToken cancellationToken)
    {
        if (ThrowSynchronouslyOnNotifyCodeIncorrect is { } synchronousException)
        {
            throw synchronousException;
        }

        IncorrectCodeNotifications.Add(code);
        return ThrowOnNotifyCodeIncorrect is { } exception ? Task.FromException(exception) : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task NotifyAttemptsExhaustedAsync(CancellationToken cancellationToken)
    {
        if (ThrowSynchronouslyOnNotifyAttemptsExhausted is { } synchronousException)
        {
            throw synchronousException;
        }

        AttemptsExhaustedCallCount++;
        return ThrowOnNotifyAttemptsExhausted is { } exception ? Task.FromException(exception) : Task.CompletedTask;
    }
}
