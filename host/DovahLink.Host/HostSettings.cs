namespace DovahLink.Host;

/// <summary>
/// User-editable host configuration, already resolved and validated by
/// <see cref="IHostSettingsProvider.Load"/>. Never carries a raw, unvalidated file value -- every
/// field here is safe to use directly.
/// </summary>
/// <param name="MaxActiveSessions">
/// The maximum number of concurrent client sessions and connections the host admits at once, per
/// <see cref="Sessions.SessionRegistry"/> and <see cref="Client.Transport.PublicWebSocketListener"/>.
/// </param>
public sealed record HostSettings(int MaxActiveSessions);
