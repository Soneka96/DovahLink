using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests the provenance-validated page-response handoff to quest collection.</summary>
public class TrackedQuestCaptureHandlerTests
{
    /// <summary>Forwards a registered quest-page capture after the generic sink validates its state area.</summary>
    [Fact]
    public void Handle_TrackedQuestPage_DelegatesValidatedContext()
    {
        var coordinator = new FakeTrackedQuestCaptureCoordinator();
        var handler = new TrackedQuestCaptureHandler(coordinator);
        LiveCaptureContext context = BuildContext([new StateAreaId(Constants.TrackedQuestsStateArea)]);

        handler.Handle(context);

        Assert.Same(context, Assert.Single(coordinator.PageCaptures));
    }

    /// <summary>Ignores a page capture context that is not bound to exactly the tracked-quest area.</summary>
    [Fact]
    public void Handle_WrongStateArea_DoesNotDelegate()
    {
        var coordinator = new FakeTrackedQuestCaptureCoordinator();
        var handler = new TrackedQuestCaptureHandler(coordinator);
        LiveCaptureContext context = BuildContext([new StateAreaId(Constants.GameTimeStateArea)]);

        handler.Handle(context);

        Assert.Empty(coordinator.PageCaptures);
    }

    /// <summary>Builds the validated live-capture metadata passed by <see cref="LiveCaptureSink"/>.</summary>
    /// <param name="stateAreas">The capture unit's declared areas.</param>
    /// <returns>A quest page capture context.</returns>
    private static LiveCaptureContext BuildContext(IReadOnlyList<StateAreaId> stateAreas)
    {
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PlayContextId playContextId = PlayContextId.NewId();
        var unit = new CaptureUnitDefinition(
            CaptureSourceKind.Sample,
            (uint)TrackedQuestCaptureKey.Page,
            RateClass: null,
            SynchronizationRole.HostOrchestratedBaseline,
            stateAreas);
        var capture = new IpcCaptureResultMessage(
            1,
            CaptureSourceKind.Sample,
            (uint)TrackedQuestCaptureKey.Page,
            CaptureAvailability.Available,
            playContextId,
            [1]);
        return new LiveCaptureContext(
            capture,
            new AdapterCaptureSource(instanceId, 1),
            unit,
            new AdapterAvailabilitySnapshot(AdapterAvailability.Available, instanceId, true, 1),
            playContextId,
            1,
            DateTimeOffset.UtcNow);
    }
}
