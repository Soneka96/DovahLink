using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.Security;
using DovahLink.Host.Time;

namespace DovahLink.Host.Composition;

/// <summary>Composes <see cref="CoreServices"/>, the host-lifetime singletons every other composed service graph depends on.</summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Constructs the core host-lifetime singletons and wires <see cref="IStateAuthorityLifecycle.FatalFailureOccurred"/>
    /// to cancel <paramref name="shutdown"/> -- the runtime-mint-failure case, per
    /// <c>plans/documentation-and-composition-normalization/01.3a-public-vocabulary-and-identity-semantics.md</c>
    /// Section C's fail-closed policy; the startup-mint-failure case is covered separately by
    /// <see cref="StateAuthorityLifecycle"/>'s own constructor propagating uncaught.
    /// </summary>
    /// <param name="shutdown">The shared shutdown source a runtime state-authority mint failure cancels.</param>
    /// <param name="hostSettingsProvider">
    /// The provider the user-configured device cap is resolved from. Defaults to the real
    /// <see cref="HostSettingsProvider"/>, reading the production settings file.
    /// </param>
    /// <returns>The composed core services.</returns>
    public static CoreServices ComposeCoreServices(CancellationTokenSource shutdown, IHostSettingsProvider? hostSettingsProvider = null)
    {
        var tracker = new AdapterAvailabilityTracker();
        var clock = new SystemClock();
        var stateAuthorityLifecycle = new StateAuthorityLifecycle(tracker);
        stateAuthorityLifecycle.FatalFailureOccurred += () => shutdown.Cancel();
        HostSettings settings = (hostSettingsProvider ?? new HostSettingsProvider()).Load();
        var securityGate = new SecurityStateGate();

        return new CoreServices(tracker, clock, settings, securityGate, stateAuthorityLifecycle);
    }
}
