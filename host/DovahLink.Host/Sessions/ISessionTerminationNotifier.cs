namespace DovahLink.Host.Sessions;

/// <summary>
/// The narrow, host-owned seam through which an administrative session invalidation requests a
/// best-effort terminal notification and forced close for one already-unauthorized session. A later
/// concept implements this over the real public WebSocket transport; this concept only depends on and
/// calls it, so <see cref="IClientSessionInvalidator"/> and its callers never own a WebSocket object.
/// </summary>
public interface ISessionTerminationNotifier
{
    /// <summary>
    /// Attempts a best-effort <c>session_invalidated</c> notification followed by a forced close for
    /// <paramref name="target"/>. The session is already unauthorized by the time this is called --
    /// <see cref="SessionRegistry"/> has already removed it -- so a notification failure never
    /// re-authorizes or otherwise preserves it. An implementation must not throw.
    /// </summary>
    /// <param name="target">The already-invalidated session to notify and close.</param>
    /// <param name="cancellationToken">The token used to bound the underlying notification.</param>
    Task NotifyAndCloseAsync(SessionInvalidationTarget target, CancellationToken cancellationToken = default);
}
