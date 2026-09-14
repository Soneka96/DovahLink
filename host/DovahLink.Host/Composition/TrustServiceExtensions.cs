using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Composition;

/// <summary>Composes <see cref="TrustServices"/>, the trust-and-session graph shared by the adapter and public client boundaries.</summary>
public static class TrustServiceExtensions
{
    /// <summary>
    /// Loads trust persistence and constructs the trust-and-session service graph. Loading -- and,
    /// per <see cref="TrustStore.CreateAsync"/>'s own contract, failing this entire call closed on
    /// malformed or undecryptable data -- happens before any other service in the returned graph is
    /// constructed, so no returned service can ever be backed by a partially loaded or silently reset
    /// trust store.
    /// </summary>
    /// <param name="core">The already-composed core services this graph is built on.</param>
    /// <param name="trustStorePersistence">
    /// The trust-store persistence adapter to load from and write through to. Defaults to the real
    /// per-Windows-user DPAPI-protected file.
    /// </param>
    /// <returns>The composed trust-and-session services.</returns>
    /// <exception cref="InvalidDataException">The persisted trust store exists but could not be decrypted or parsed.</exception>
    public static async Task<TrustServices> ComposeTrustServicesAsync(CoreServices core, ITrustStorePersistence? trustStorePersistence = null)
    {
        ITrustStore trustStore = await TrustStore.CreateAsync(
            trustStorePersistence ?? new WindowsDpapiTrustStorePersistence(), core.Clock, core.SecurityGate);
        var sessionRegistry = new SessionRegistry(core.SecurityGate, core.Settings.MaxActiveSessions);
        var pairingCoordinator = new PairingCoordinator(trustStore, core.Clock);
        var playContextTracker = new PlayContextTracker();
        var envelopeCodec = new PublicEnvelopeCodec(core.StateAuthorityLifecycle);
        var connectionRegistry = new PublicSessionConnectionRegistry();

        ISessionTerminationNotifier terminationNotifier = new PublicSessionTerminationNotifier(connectionRegistry, envelopeCodec, playContextTracker);
        IClientSessionInvalidator sessionInvalidator = new ClientSessionInvalidator(sessionRegistry, terminationNotifier);
        ITrustAdminService trustAdminService = new TrustAdminService(trustStore, sessionInvalidator, pairingCoordinator);
        ITrustResetService trustResetService = new TrustResetService(trustStore, sessionInvalidator, pairingCoordinator, core.Clock);
        IAdapterTrustAdminRequestHandler trustAdminRequestHandler = new AdapterTrustAdminRequestHandler(trustAdminService, trustResetService, core.Clock);

        return new TrustServices(
            trustStore, sessionRegistry, pairingCoordinator, playContextTracker, envelopeCodec, connectionRegistry, trustAdminService, trustAdminRequestHandler);
    }
}
