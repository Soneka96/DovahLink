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
    /// to cancel <paramref name="shutdown"/> -- the runtime-mint-failure case, per
    /// <c>plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md</c>
    /// Section C's fail-closed policy; the startup-mint-failure case is covered separately by
    /// <see cref="StateAuthorityLifecycle"/>'s own constructor propagating uncaught.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="clock">
    /// The clock this Host lifetime uses, already constructed so the fail-closed trust-store
    /// bootstrap (<see cref="TrustServiceExtensions.CreateTrustStoreAsync"/>) can run before the
    /// container is built. Registered as this exact instance, so every downstream consumer resolves
    /// the same one through <see cref="IServiceProvider"/> rather than a second, independent copy.
    /// </param>
    /// <param name="securityGate">
    /// The security gate this Host lifetime uses, for the same pre-container bootstrap reason as
    /// <paramref name="clock"/>. Registered as this exact instance.
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
