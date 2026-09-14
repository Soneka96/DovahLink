using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Dispatch;

namespace DovahLink.Host.Composition;

/// <summary>
/// The private adapter-IPC boundary: host-lifetime singletons, constructed once per
/// <see cref="Program.ComposeAndRunAsync"/> call. See
/// <see cref="AdapterIpcServiceExtensions.ComposeAdapterIpcServices"/>.
/// </summary>
/// <param name="Verifier">
/// Host-lifetime singleton verifying a connecting adapter's peer-ownership proof; also the source of
/// the rendezvous-published proof token and HostProof HMAC key.
/// </param>
/// <param name="Listener">
/// Host-lifetime singleton accepting the adapter's IPC connection. Constructed here but still
/// disposed by <see cref="Program.ComposeAndRunAsync"/>'s own <see langword="using"/> declaration --
/// this record only makes its construction explicit, not its ownership.
/// </param>
/// <param name="Notifier">Host-lifetime singleton notifying the connected adapter of pairing display/outcome events.</param>
public sealed record AdapterIpcServices(
    IAdapterPeerProofVerifier Verifier,
    IAdapterIpcListener Listener,
    IPairingAdapterNotifier Notifier);
