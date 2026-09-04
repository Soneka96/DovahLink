using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// An immutable snapshot of one session an administrative trust mutation invalidated, captured while
/// the registry's internal lock is held and handed back only after it is released, so no transport
/// send or close ever runs under that lock. Used by <see cref="IClientSessionInvalidator"/> to attempt
/// a best-effort terminal notification and forced close without the trust, pairing, or session layer
/// ever owning a WebSocket object.
/// </summary>
/// <param name="SessionId">The invalidated session's identifier.</param>
/// <param name="ConnectionId">The transport connection that owned the invalidated session.</param>
/// <param name="ClientId">The client the invalidated session belonged to.</param>
/// <param name="Reason">The authoritative reason this session was invalidated.</param>
/// <param name="AuthenticationSource">How the invalidated session's owning connection authenticated at <c>hello</c>.</param>
public sealed record SessionInvalidationTarget(
    SessionId SessionId,
    ConnectionId ConnectionId,
    ClientId ClientId,
    SessionInvalidationReason Reason,
    SessionAuthenticationSource AuthenticationSource);
