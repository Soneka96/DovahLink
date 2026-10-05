using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Routes validated private quest-page responses to their pending page read.</summary>
public sealed class TrackedQuestCaptureHandler : ILiveCaptureHandler
{
    /// <summary>The one raw page-response capture identity owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)TrackedQuestCaptureKey.Page),
        ]);

    /// <summary>Owns request correlation and provenance matching for private pages.</summary>
    private readonly ITrackedQuestPageReader pageReader;

    /// <summary>Creates the page-response adapter over the private page reader.</summary>
    /// <param name="pageReader">Receives each validated page response.</param>
    public TrackedQuestCaptureHandler(ITrackedQuestPageReader pageReader)
    {
        this.pageReader = pageReader;
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

        pageReader.AcceptPageCapture(context);
    }
}
