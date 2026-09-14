using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Bootstraps and registers the trust-and-session graph shared by the adapter and public client boundaries.</summary>
public static class TrustServiceExtensions
{
    /// <summary>
    /// Loads trust persistence and constructs the trust store -- failing this call closed, per
    /// <see cref="TrustStore.CreateAsync"/>'s own contract, on malformed or undecryptable data. Runs
    /// before the service collection is built: every other service in this graph depends on an
    /// already-successfully-loaded trust store existing, so none of them can ever be backed by a
    /// partially loaded or silently reset one.
    /// </summary>
    /// <param name="clock">The already-constructed clock this Host lifetime uses.</param>
    /// <param name="securityGate">The already-constructed security gate this Host lifetime uses.</param>
    /// <param name="trustStorePersistence">
    /// The trust-store persistence adapter to load from and write through to. Defaults to the real
    /// per-Windows-user DPAPI-protected file.
    /// </param>
    /// <returns>The successfully loaded trust store, to register via <see cref="AddTrustServices"/>.</returns>
    /// <exception cref="InvalidDataException">The persisted trust store exists but could not be decrypted or parsed.</exception>
    public static async Task<ITrustStore> CreateTrustStoreAsync(IClock clock, ISecurityStateGate securityGate, ITrustStorePersistence? trustStorePersistence = null) =>
        await TrustStore.CreateAsync(trustStorePersistence ?? new WindowsDpapiTrustStorePersistence(), clock, securityGate);

    /// <summary>
    /// Registers the trust-and-session graph shared by adapter-originated trust-admin requests and by
    /// the public client boundary. Requires <see cref="CoreServiceExtensions.AddCoreServices"/> to
    /// already be registered on <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="trustStore">The already-successfully-loaded trust store, from <see cref="CreateTrustStoreAsync"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTrustServices(this IServiceCollection services, ITrustStore trustStore)
    {
        services.AddSingleton(trustStore);
        services.AddSingleton(sp => new SessionRegistry(sp.GetRequiredService<ISecurityStateGate>(), sp.GetRequiredService<HostSettings>().MaxActiveSessions));
        services.AddSingleton<ISessionRegistry>(sp => sp.GetRequiredService<SessionRegistry>());
        services.AddSingleton(sp => new PairingCoordinator(sp.GetRequiredService<ITrustStore>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<IPairingCoordinator>(sp => sp.GetRequiredService<PairingCoordinator>());
        services.AddSingleton<IPlayContextTracker, PlayContextTracker>();
        services.AddSingleton<IPublicEnvelopeCodec>(sp => new PublicEnvelopeCodec(sp.GetRequiredService<IStateAuthorityLifecycle>()));
        services.AddSingleton<IPublicSessionConnectionRegistry, PublicSessionConnectionRegistry>();
        services.AddSingleton<ISessionTerminationNotifier>(sp => new PublicSessionTerminationNotifier(
            sp.GetRequiredService<IPublicSessionConnectionRegistry>(), sp.GetRequiredService<IPublicEnvelopeCodec>(), sp.GetRequiredService<IPlayContextTracker>()));
        services.AddSingleton<IClientSessionInvalidator>(sp => new ClientSessionInvalidator(
            sp.GetRequiredService<ISessionRegistry>(), sp.GetRequiredService<ISessionTerminationNotifier>()));
        services.AddSingleton<ITrustAdminService>(sp => new TrustAdminService(
            sp.GetRequiredService<ITrustStore>(), sp.GetRequiredService<IClientSessionInvalidator>(), sp.GetRequiredService<IPairingCoordinator>()));
        services.AddSingleton<ITrustResetService>(sp => new TrustResetService(
            sp.GetRequiredService<ITrustStore>(), sp.GetRequiredService<IClientSessionInvalidator>(), sp.GetRequiredService<IPairingCoordinator>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<IAdapterTrustAdminRequestHandler>(sp => new AdapterTrustAdminRequestHandler(
            sp.GetRequiredService<ITrustAdminService>(), sp.GetRequiredService<ITrustResetService>(), sp.GetRequiredService<IClock>()));

        return services;
    }
}
