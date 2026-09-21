using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Process;
using DovahLink.Host.State;
using DovahLink.Host.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the private adapter-IPC boundary.</summary>
public static class AdapterIpcServiceExtensions
{
    /// <summary>
    /// Registers the adapter-IPC listener and its collaborators. Every accepted adapter connection
    /// gets its own <see cref="AdapterIpcConnection"/>/<see cref="AdapterIpcSession"/> pair, built
    /// fresh by the composed <see cref="IAdapterConnectionFactory"/> -- connection-scoped, never
    /// shared across two accepted connections. Requires
    /// <see cref="CoreServiceExtensions.AddCoreServices"/> and
    /// <see cref="TrustServiceExtensions.AddTrustServices"/> to already be registered on
    /// <paramref name="services"/>. <see cref="LiveCaptureSink"/>, <see cref="LiveStateScheduler"/>,
    /// and the singleton <see cref="ResynchronizationPlan"/> also resolve <see cref="LiveStateCatalog"/>.
    /// The plan is shared by every connection-owned session. The explicitly registered
    /// <see cref="CharacterCaptureHandler"/> resolves the typed publishers and
    /// <see cref="LiveStateApplication"/>, which resolves <see cref="IStatePublicationSink"/>, so
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> must be registered too
    /// before the composed provider is built -- registration order across these methods does not
    /// otherwise matter, since every dependency here resolves lazily at first use.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="listenerPort">The private adapter-IPC loopback port to bind, or zero to let the operating system assign one.</param>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity, verified against every accepted connection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAdapterIpcServices(this IServiceCollection services, int listenerPort, OwnerLifetimeId ownerLifetimeId)
    {
        services.AddSingleton(new HostInstanceOptions(ownerLifetimeId));
        services.AddSingleton(new AdapterIpcOptions(listenerPort));

        services.AddSingleton<IAdapterConnectionLifecycle, AdapterConnectionLifecycle>();
        services.AddSingleton<IAdapterPeerProofVerifier, AdapterPeerProofVerifier>();
        services.AddSingleton<IIpcFrameCodec, IpcFrameCodec>();
        services.AddSingleton<IAdapterContinuityRecovery, AdapterContinuityRecovery>();
        services.AddSingleton<ResynchronizationPlan>(sp =>
            sp.GetRequiredService<LiveStateCatalog>().BuildResynchronizationPlan());
        services.AddSingleton<IAdapterConnectionFactory, AdapterConnectionFactory>();
        services.AddSingleton<IAdapterIpcListener, AdapterIpcListener>();
        services.AddSingleton<IPairingAdapterNotifier, AdapterPairingNotifier>();
        services.AddSingleton<IRevisionTracker, RevisionTracker>();
        services.AddSingleton<IStatePublisher<float?>, StatePublisher<float?>>();
        services.AddSingleton<IStatePublisher<ushort?>, StatePublisher<ushort?>>();
        services.AddSingleton<IResynchronizationTransactionCoordinator>(sp => new ResynchronizationTransactionCoordinator(
            sp.GetRequiredService<LiveStateCatalog>(),
            sp.GetRequiredService<IAdapterAvailabilityTracker>(),
            sp.GetRequiredService<IAdapterContinuityRecovery>(),
            Constants.ResynchronizationTransactionTimeout));
        services.AddSingleton<ILiveStateApplication, LiveStateApplication>();
        services.AddSingleton<CharacterCaptureHandler>();
        services.AddSingleton<ILiveCaptureHandler>(sp => sp.GetRequiredService<CharacterCaptureHandler>());
        services.AddSingleton<ILiveCaptureSink, LiveCaptureSink>();
        services.AddSingleton<LiveStateScheduler>();
        services.AddSingleton<ILiveStateScheduler>(sp => sp.GetRequiredService<LiveStateScheduler>());
        services.AddSingleton<IPlayContextResynchronizationTrigger, PlayContextResynchronizationTrigger>();

        return services;
    }
}
