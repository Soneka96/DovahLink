using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Composition;

/// <summary>
/// The trust-and-session graph shared by adapter-originated trust-admin requests and by the public
/// client boundary -- one instance graph both sides compose over. Host-lifetime singletons,
/// constructed once per <see cref="Program.ComposeAndRunAsync"/> call. See
/// <see cref="TrustServiceExtensions.ComposeTrustServicesAsync"/>.
/// </summary>
/// <param name="TrustStore">Host-lifetime singleton owning the durable trust domain.</param>
/// <param name="SessionRegistry">
/// Host-lifetime singleton tracking active sessions. Exposed as the concrete type, not
/// <see cref="ISessionRegistry"/>, because <see cref="Program.ComposeAndRunAsync"/>'s
/// <c>onComposed</c> test-observability callback requires it.
/// </param>
/// <param name="PairingCoordinator">
/// Host-lifetime singleton owning pairing state. Exposed as the concrete type for the same
/// test-observability reason as <paramref name="SessionRegistry"/>.
/// </param>
/// <param name="PlayContextTracker">Host-lifetime singleton tracking the current play context.</param>
/// <param name="EnvelopeCodec">Host-lifetime singleton encoding and decoding the public wire envelope.</param>
/// <param name="ConnectionRegistry">Host-lifetime singleton resolving a session's exact live connection.</param>
/// <param name="TrustAdminService">Host-lifetime singleton implementing Known Device administration.</param>
/// <param name="TrustAdminRequestHandler">Host-lifetime singleton handling adapter-originated trust-admin IPC requests.</param>
public sealed record TrustServices(
    ITrustStore TrustStore,
    SessionRegistry SessionRegistry,
    PairingCoordinator PairingCoordinator,
    IPlayContextTracker PlayContextTracker,
    IPublicEnvelopeCodec EnvelopeCodec,
    IPublicSessionConnectionRegistry ConnectionRegistry,
    ITrustAdminService TrustAdminService,
    IAdapterTrustAdminRequestHandler TrustAdminRequestHandler);
