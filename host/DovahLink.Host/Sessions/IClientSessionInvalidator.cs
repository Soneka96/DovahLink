using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// The narrow host-owned abstraction administrative trust mutations invalidate sessions through,
/// keeping trust administration free of direct <see cref="ISessionRegistry"/> access and of any
/// WebSocket implementation type. Applies the authoritative-mutation -> unauthorized -> best-effort
/// notification -> forced-close ordering: the registry's invalidation completes -- the affected
/// sessions are already unauthorized -- before any notification is attempted.
/// </summary>
public interface IClientSessionInvalidator
{
    /// <summary>
    /// Invalidates every currently active session belonging to one client for an explicit
    /// administrative reason, then best-effort notifies and closes each invalidated session.
    /// </summary>
    /// <param name="clientId">The client whose sessions to invalidate.</param>
    /// <param name="reason">The authoritative reason this client's sessions are being invalidated.</param>
    /// <param name="cancellationToken">The token used to bound the underlying notifications.</param>
    Task InvalidateClientAsync(ClientId clientId, SessionInvalidationReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unconditionally invalidates every currently active session for an explicit administrative
    /// reason, then best-effort notifies and closes each invalidated session.
    /// </summary>
    /// <param name="reason">The authoritative reason every session is being invalidated.</param>
    /// <param name="cancellationToken">The token used to bound the underlying notifications.</param>
    Task InvalidateAllAsync(SessionInvalidationReason reason, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IClientSessionInvalidator"/>
public sealed class ClientSessionInvalidator : IClientSessionInvalidator
{
    /// <summary>The registry invalidated sessions are removed from.</summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>The seam used to attempt each invalidated session's best-effort terminal notification and close.</summary>
    private readonly ISessionTerminationNotifier terminationNotifier;

    /// <summary>Creates a client session invalidator.</summary>
    /// <param name="sessionRegistry">The registry invalidated sessions are removed from.</param>
    /// <param name="terminationNotifier">The seam used to notify and close each invalidated session.</param>
    public ClientSessionInvalidator(ISessionRegistry sessionRegistry, ISessionTerminationNotifier terminationNotifier)
    {
        this.sessionRegistry = sessionRegistry;
        this.terminationNotifier = terminationNotifier;
    }

    /// <inheritdoc/>
    public async Task InvalidateClientAsync(ClientId clientId, SessionInvalidationReason reason, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = sessionRegistry.InvalidateAllForClient(clientId, reason);
        await NotifyAllAsync(targets, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task InvalidateAllAsync(SessionInvalidationReason reason, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = sessionRegistry.InvalidateAll(reason);
        await NotifyAllAsync(targets, cancellationToken);
    }

    /// <summary>
    /// Best-effort notifies and closes every target, never letting one target's failure prevent the
    /// remaining targets from being attempted or propagate back to the authoritative trust mutation
    /// that already committed.
    /// </summary>
    /// <param name="targets">The sessions already invalidated and awaiting notification.</param>
    /// <param name="cancellationToken">The token used to bound each underlying notification.</param>
    private async Task NotifyAllAsync(IReadOnlyList<SessionInvalidationTarget> targets, CancellationToken cancellationToken)
    {
        foreach (SessionInvalidationTarget target in targets)
        {
            try
            {
                await terminationNotifier.NotifyAndCloseAsync(target, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best-effort: the session is already unauthorized regardless of this outcome, so one
                // target's notification/close failure must never prevent the rest from being attempted
                // or propagate back to the trust mutation that already committed.
            }
        }
    }
}
