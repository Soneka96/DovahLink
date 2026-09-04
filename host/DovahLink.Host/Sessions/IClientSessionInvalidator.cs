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
    /// Invalidates every currently active session belonging to any of several clients in one atomic
    /// registry pass -- see <see cref="ISessionRegistry.InvalidateAllForClients"/> -- before attempting
    /// any target's best-effort notification/close, for an explicit administrative reason affecting
    /// multiple clients at once (for example Reset Trust revoking several devices). Unlike a sequential
    /// per-client <see cref="InvalidateClientAsync"/> loop, this guarantees every affected client is
    /// already unauthorized before any of their sessions' teardown begins.
    /// </summary>
    /// <param name="clientIds">The clients whose sessions to invalidate.</param>
    /// <param name="reason">The authoritative reason these clients' sessions are being invalidated.</param>
    /// <param name="cancellationToken">The token used to bound the underlying notifications.</param>
    Task InvalidateClientsAsync(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason, CancellationToken cancellationToken = default);

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
    public async Task InvalidateClientsAsync(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = sessionRegistry.InvalidateAllForClients(clientIds, reason);
        await NotifyAllAsync(targets, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task InvalidateAllAsync(SessionInvalidationReason reason, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = sessionRegistry.InvalidateAll(reason);
        await NotifyAllAsync(targets, cancellationToken);
    }

    /// <summary>
    /// Best-effort notifies and closes every target, never letting one target's failure or
    /// cancellation prevent the remaining targets from being attempted or propagate back to the
    /// authoritative trust mutation that already committed. Every target here is already
    /// unauthorized -- removed from the registry before this ever runs -- so a caller's own token
    /// still bounds each individual <see cref="ISessionTerminationNotifier.NotifyAndCloseAsync"/> call,
    /// but its cancellation is treated the same as any other per-target failure rather than aborting
    /// the remaining targets' teardown.
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
            catch
            {
                // Best-effort: the session is already unauthorized regardless of this outcome, so one
                // target's notification/close failure or cancellation must never prevent the rest from
                // being attempted or propagate back to the trust mutation that already committed.
            }
        }
    }
}
