using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.Security;
using DovahLink.Host.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Composition;

/// <summary>Registers the host-lifetime singletons every other composed service graph depends on.</summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Registers the core host-lifetime singletons and wires <see cref="IStateAuthorityLifecycle.FatalFailureOccurred"/>
    /// to cancel <paramref name="shutdown"/> -- the fail-closed response to a runtime state-authority
    /// mint failure; a startup-time mint failure is covered separately by
    /// <see cref="StateAuthorityLifecycle"/>'s own constructor propagating uncaught.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="clock">
    /// The clock this Host lifetime uses; registered as this exact instance, so every downstream
    /// consumer resolves the same one rather than a second, independent copy.
    /// </param>
    /// <param name="securityGate">
    /// The security gate this Host lifetime uses; registered as this exact instance, for the same
    /// reason as <paramref name="clock"/>.
    /// </param>
    /// <param name="shutdown">The shared shutdown source a runtime state-authority mint failure cancels.</param>
    /// <param name="hostSettingsProvider">
    /// The provider the user-configured device cap is resolved from. Defaults to the real
    /// <see cref="HostSettingsProvider"/>, reading the production settings file.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IClock clock,
        ISecurityStateGate securityGate,
        CancellationTokenSource shutdown,
        IHostSettingsProvider? hostSettingsProvider = null)
    {
        HostSettings settings = (hostSettingsProvider ?? new HostSettingsProvider()).Load();

        services.AddSingleton(clock);
        services.AddSingleton(securityGate);
        services.AddSingleton(settings);
        services.AddSingleton<IAdapterAvailabilityTracker, AdapterAvailabilityTracker>();
        services.AddSingleton<IStateAuthorityLifecycle>(sp =>
        {
            var lifecycle = new StateAuthorityLifecycle(sp.GetRequiredService<IAdapterAvailabilityTracker>());
            lifecycle.FatalFailureOccurred += () => shutdown.Cancel();
            return lifecycle;
        });

        return services;
    }
}
