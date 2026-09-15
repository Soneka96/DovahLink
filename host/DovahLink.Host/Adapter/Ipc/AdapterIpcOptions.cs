namespace DovahLink.Host.Adapter.Ipc;

/// <summary>The private adapter-IPC listener's own runtime configuration.</summary>
/// <param name="ListenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
public sealed record AdapterIpcOptions(int ListenerPort);
