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
    /// <see cref="IHostRuntime"/> itself -- automatic constructor resolution supplies
    /// <see cref="DovahLinkHostRuntime"/>'s optional <c>publicListener</c> parameter, per its own
    /// contract. Requires <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/> (which
    /// registers the <see cref="HostInstanceOptions"/> this method's own registrations resolve) and
    /// <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> to already be registered on
    /// <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="lifetime">The host lifetime <see cref="IHostRuntime"/> runs until the process is asked to exit.</param>
    /// <param name="rendezvousOutput">Where <see cref="IHostRuntime"/> reports the rendezvous endpoint for a launching adapter to read.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddHostRuntime(this IServiceCollection services, IHostProcessLifetime lifetime, TextWriter rendezvousOutput)
    {
        services.AddSingleton(lifetime);
        services.AddSingleton(rendezvousOutput);

        services.AddSingleton<IHostShutdownSignal, NamedEventHostShutdownSignal>();
        services.AddSingleton<IHostRendezvousPublisher, FileHostRendezvousPublisher>();
        services.AddSingleton<IHostRuntime, DovahLinkHostRuntime>();

        return services;
    }
}
