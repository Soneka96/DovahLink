using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Composition;

/// <summary>
/// The public client boundary: a host-lifetime singleton, constructed once per
/// <see cref="Program.ComposeAndRunAsync"/> call. See
/// <see cref="PublicClientServiceExtensions.ComposePublicClientServices"/>.
/// </summary>
/// <param name="Listener">
/// Host-lifetime singleton accepting public client connections, or <see langword="null"/> when no
/// public listener port was supplied. Constructed here but still disposed by
/// <see cref="Program.ComposeAndRunAsync"/>'s own <see langword="using"/> declaration -- this record
/// only makes its construction explicit, not its ownership.
/// </param>
public sealed record PublicClientServices(IPublicWebSocketListener? Listener);
