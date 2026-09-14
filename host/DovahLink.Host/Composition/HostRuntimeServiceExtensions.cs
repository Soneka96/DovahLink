using DovahLink.Host;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Process;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the explicit Host lifecycle orchestrator and its remaining collaborators.</summary>
public static class HostRuntimeServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IHostShutdownSignal"/>, <see cref="IHostRendezvousPublisher"/>, and
    /// <see cref="DovahLinkHostRuntime"/> itself. <see cref="DovahLinkHostRuntime"/>'s own registration
    /// resolves <see cref="IPublicWebSocketListener"/> with <see cref="ServiceProviderServiceExtensions.GetService{T}"/>,
    /// not <c>GetRequiredService</c>, so it is <see langword="null"/> exactly when
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> left it unregistered.
    /// Requires <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/> and
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> to already be registered on
    /// <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="ownerLifetimeId">The owning Skyrim process's lifetime identity, used to derive the named shutdown signal and rendezvous file path.</param>
    /// <param name="lifetime">The host lifetime <see cref="DovahLinkHostRuntime"/> runs until the process is asked to exit.</param>
    /// <param name="rendezvousOutput">Where <see cref="DovahLinkHostRuntime"/> reports the rendezvous endpoint for a launching adapter to read.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddHostRuntime(
        this IServiceCollection services, OwnerLifetimeId ownerLifetimeId, IHostProcessLifetime lifetime, TextWriter rendezvousOutput)
    {
        services.AddSingleton<IHostShutdownSignal>(_ => new NamedEventHostShutdownSignal(Constants.ShutdownEventName(ownerLifetimeId)));
        services.AddSingleton<IHostRendezvousPublisher>(_ => new FileHostRendezvousPublisher(Constants.RendezvousFilePath(ownerLifetimeId)));
        services.AddSingleton(sp =>
        {
            IAdapterPeerProofVerifier verifier = sp.GetRequiredService<IAdapterPeerProofVerifier>();
            return new DovahLinkHostRuntime(
                sp.GetRequiredService<IAdapterIpcListener>(),
                sp.GetService<IPublicWebSocketListener>(),
                sp.GetRequiredService<IHostShutdownSignal>(),
                lifetime,
                sp.GetRequiredService<IHostRendezvousPublisher>(),
                rendezvousOutput,
                verifier.ExpectedToken,
                verifier.HostProofKey);
        });

        return services;
    }
}
