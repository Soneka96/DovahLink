using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A configurable in-memory stand-in for <see cref="ISessionTerminationNotifier"/>.</summary>
public sealed class FakeSessionTerminationNotifier : ISessionTerminationNotifier
{
    /// <summary>Every target this notifier was asked to notify and close, in call order.</summary>
    public List<SessionInvalidationTarget> NotifiedTargets { get; } = [];

    /// <summary>When set, <see cref="NotifyAndCloseAsync"/> returns a faulted task carrying this exception instead of completing.</summary>
    public Exception? ThrowOnNotify { get; set; }

    /// <summary>
    /// Optional hook invoked synchronously for each target before it is recorded, letting a test
    /// observe collaborator state (for example that the session is already unauthorized) at the exact
    /// moment notification is attempted.
    /// </summary>
    public Action<SessionInvalidationTarget>? OnNotify { get; set; }

    /// <summary>
    /// Optional asynchronous work awaited for each target before it is recorded, letting a test hold
    /// one specific target's notification open (for example by keying on its <c>ClientId</c>) while
    /// asserting on another target's already-invalidated state in the meantime.
    /// </summary>
    public Func<SessionInvalidationTarget, Task>? BeforeNotify { get; set; }

    /// <inheritdoc/>
    public async Task NotifyAndCloseAsync(SessionInvalidationTarget target, CancellationToken cancellationToken = default)
    {
        if (BeforeNotify is { } beforeNotify)
        {
            await beforeNotify(target);
        }

        OnNotify?.Invoke(target);
        NotifiedTargets.Add(target);
        if (ThrowOnNotify is { } exception)
        {
            throw exception;
        }
    }
}
