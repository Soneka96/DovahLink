using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>A record of one client connection's session and the client it belongs to.</summary>
/// <param name="SessionId">The session's identifier, valid only for the connection it was created for.</param>
/// <param name="ClientId">The client the session belongs to.</param>
/// <param name="ConnectionId">The transport connection that owns the session.</param>
/// <param name="State">The session's current lifecycle state.</param>
/// <param name="AuthenticationSource">How this session's owning connection authenticated at <c>hello</c>.</param>
/// <param name="TrustTier">This session's current message-authorization tier.</param>
public sealed record ActiveSessionRecord(
    SessionId SessionId,
    ClientId ClientId,
    ConnectionId ConnectionId,
    SessionState State,
    SessionAuthenticationSource AuthenticationSource,
    SessionTrustTier TrustTier);
