namespace DovahLink.Host.Client.Transport;

/// <summary>The public client listener's own runtime configuration.</summary>
/// <param name="Port">The public loopback port to bind, or zero to let the operating system assign one.</param>
public sealed record PublicListenerOptions(int Port);
