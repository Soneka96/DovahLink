using DovahLink.Host;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Process;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the explicit Host lifecycle orchestrator and its remaining collaborators.</summary>
public static class HostRuntimeServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IHostShutdownSignal"/>, <see cref="IHostRendezvousPublisher"/>, and
    /// <see cref="DovahLinkHostRuntime"/> itself. <see cref="DovahLinkHostRuntime"/>'s own
    /// <c>publicListener</c> constructor parameter defaults to <see langword="null"/>, so automatic
    /// constructor resolution supplies <see langword="null"/> exactly when
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> left
    /// <see cref="IPublicWebSocketListener"/> unregistered, rather than throwing. Requires
    /// <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/> (which registers the
    /// <see cref="HostInstanceOptions"/> this method's own registrations resolve) and
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> to already be registered on
    /// <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="lifetime">The host lifetime <see cref="DovahLinkHostRuntime"/> runs until the process is asked to exit.</param>
    /// <param name="rendezvousOutput">Where <see cref="DovahLinkHostRuntime"/> reports the rendezvous endpoint for a launching adapter to read.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddHostRuntime(this IServiceCollection services, IHostProcessLifetime lifetime, TextWriter rendezvousOutput)
    {
        services.AddSingleton(lifetime);
        services.AddSingleton(rendezvousOutput);

        services.AddSingleton<IHostShutdownSignal, NamedEventHostShutdownSignal>();
        services.AddSingleton<IHostRendezvousPublisher, FileHostRendezvousPublisher>();
        services.AddSingleton<DovahLinkHostRuntime>();

        return services;
    }
}
