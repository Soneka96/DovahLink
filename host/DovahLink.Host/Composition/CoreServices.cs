using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.Security;
using DovahLink.Host.Time;

namespace DovahLink.Host.Composition;

/// <summary>
/// The foundational host-lifetime singletons every other composed service graph depends on --
/// constructed once per <see cref="Program.ComposeAndRunAsync"/> call and shared for the whole
/// process lifetime. See <see cref="CoreServiceExtensions.ComposeCoreServices"/>.
/// </summary>
/// <param name="AdapterAvailability">Host-lifetime singleton tracking adapter connection availability.</param>
/// <param name="Clock">Host-lifetime singleton providing the current time.</param>
/// <param name="Settings">The resolved, validated host configuration for this process lifetime.</param>
/// <param name="SecurityGate">Host-lifetime singleton synchronizing trust mutation and session-authorization observation.</param>
/// <param name="StateAuthorityLifecycle">Host-lifetime singleton owning the state-authority continuity-epoch rotation.</param>
public sealed record CoreServices(
    IAdapterAvailabilityTracker AdapterAvailability,
    IClock Clock,
    HostSettings Settings,
    ISecurityStateGate SecurityGate,
    IStateAuthorityLifecycle StateAuthorityLifecycle);
