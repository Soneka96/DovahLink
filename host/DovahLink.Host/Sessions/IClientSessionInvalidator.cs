using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// The narrow host-owned abstraction administrative trust mutations invalidate sessions through,
/// keeping trust administration free of direct <see cref="ISessionRegistry"/> access and of any
/// WebSocket implementation type. Deliberately separates authoritative session-authorization removal
/// (the synchronous <c>InvalidateClient</c>/<c>InvalidateClients</c>/<c>InvalidateAll</c> members) from
/// best-effort terminal notification and close (<see cref="NotifyAndCloseAllAsync"/>): a caller that
/// also needs to cancel pairing state as part of the same administrative mutation can do so between
/// the two, so the full ordering becomes authoritative mutation -> sessions unauthorized -> pairing
/// cancelled -> best-effort notification -> forced close, per
/// <c>ai/context/protocol/security.md</c>'s "Administrative session invalidation".
/// </summary>
public interface IClientSessionInvalidator
{
    /// <summary>
    /// Invalidates every currently active session belonging to one client for an explicit
    /// administrative reason. Synchronous and immediate: the affected sessions are unauthorized in
    /// <see cref="ISessionRegistry"/> before this call returns, well before any later best-effort
    /// notification is attempted.
    /// </summary>
    /// <param name="clientId">The client whose sessions to invalidate.</param>
    /// <param name="reason">The authoritative reason this client's sessions are being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call actually invalidated, for a later <see cref="NotifyAndCloseAllAsync"/> call.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateClient(ClientId clientId, SessionInvalidationReason reason);

    /// <summary>
    /// Invalidates every currently active session belonging to any of several clients in one atomic
    /// registry pass -- see <see cref="ISessionRegistry.InvalidateAllForClients"/> -- for an explicit
    /// administrative reason affecting multiple clients at once (for example Reset Trust revoking
    /// several devices). Unlike a sequential per-client <see cref="InvalidateClient"/> loop, this
    /// guarantees every affected client is already unauthorized before any of their sessions' teardown
    /// begins.
    /// </summary>
    /// <param name="clientIds">The clients whose sessions to invalidate.</param>
    /// <param name="reason">The authoritative reason these clients' sessions are being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call actually invalidated, across every client, for a later <see cref="NotifyAndCloseAllAsync"/> call.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateClients(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason);

    /// <summary>
    /// Unconditionally invalidates every currently active session for an explicit administrative
    /// reason. Synchronous and immediate, the same as <see cref="InvalidateClient"/>.
    /// </summary>
    /// <param name="reason">The authoritative reason every session is being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call invalidated, for a later <see cref="NotifyAndCloseAllAsync"/> call.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateAll(SessionInvalidationReason reason);

    /// <summary>
    /// Attempts a best-effort terminal notification and forced close for every already-invalidated
    /// target returned by an earlier <see cref="InvalidateClient"/>, <see cref="InvalidateClients"/>,
    /// or <see cref="InvalidateAll"/> call. One target's notification/close failure or cancellation
    /// never prevents the remaining targets from being attempted, and never re-authorizes or otherwise
    /// restores any target: every target here is already unauthorized regardless of this call's own
    /// outcome.
    /// </summary>
    /// <param name="targets">The sessions already invalidated and awaiting notification.</param>
    /// <param name="cancellationToken">The token used to bound each underlying notification.</param>
    Task NotifyAndCloseAllAsync(IReadOnlyList<SessionInvalidationTarget> targets, CancellationToken cancellationToken = default);
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
    public IReadOnlyList<SessionInvalidationTarget> InvalidateClient(ClientId clientId, SessionInvalidationReason reason) =>
        sessionRegistry.InvalidateAllForClient(clientId, reason);

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateClients(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason) =>
        sessionRegistry.InvalidateAllForClients(clientIds, reason);

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAll(SessionInvalidationReason reason) =>
        sessionRegistry.InvalidateAll(reason);

    /// <inheritdoc/>
    /// <remarks>
    /// Best-effort notifies and closes every target, never letting one target's failure or
    /// cancellation prevent the remaining targets from being attempted or propagate back to the
    /// authoritative mutation that already committed. Every target here is already unauthorized --
    /// removed from the registry by an earlier invalidate call -- so a caller's own token still bounds
    /// each individual <see cref="ISessionTerminationNotifier.NotifyAndCloseAsync"/> call, but its
    /// cancellation is treated the same as any other per-target failure rather than aborting the
    /// remaining targets' teardown.
    /// </remarks>
    public async Task NotifyAndCloseAllAsync(IReadOnlyList<SessionInvalidationTarget> targets, CancellationToken cancellationToken = default)
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
