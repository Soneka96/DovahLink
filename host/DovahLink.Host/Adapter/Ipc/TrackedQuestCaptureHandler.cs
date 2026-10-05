using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Routes validated private quest-page responses to the Host collection coordinator.</summary>
public sealed class TrackedQuestCaptureHandler : ILiveCaptureHandler
{
    /// <summary>The one raw page-response capture identity owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)TrackedQuestCaptureKey.Page),
        ]);

    /// <summary>Owns page correlation and complete quest collection.</summary>
    private readonly ITrackedQuestCaptureCoordinator coordinator;

    /// <summary>Creates the page-response adapter over the current complete-quest collector.</summary>
    /// <param name="coordinator">Receives each validated page response.</param>
    public TrackedQuestCaptureHandler(ITrackedQuestCaptureCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures => supportedCaptures;

    /// <inheritdoc/>
    public void Handle(LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1
            || context.CaptureUnit.StateAreas[0] != new StateAreaId(Constants.TrackedQuestsStateArea))
        {
            return;
        }

        coordinator.AcceptPageCapture(context);
    }
}
