using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>A record of one client connection's session and the client it belongs to.</summary>
/// <param name="SessionId">The session's identifier, valid only for the connection it was created for.</param>
/// <param name="ClientId">The client the session belongs to.</param>
/// <param name="State">The session's current lifecycle state.</param>
public sealed record ActiveSessionRecord(SessionId SessionId, ClientId ClientId, SessionState State);
