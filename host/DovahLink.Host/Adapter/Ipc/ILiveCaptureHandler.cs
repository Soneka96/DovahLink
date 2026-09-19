namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Handles captures for the explicitly declared source and key identities it owns.</summary>
public interface ILiveCaptureHandler
{
    /// <summary>The source and key identities this handler accepts.</summary>
    IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures { get; }

    /// <summary>Decodes and applies one capture after generic provenance and context validation.</summary>
    /// <param name="context">The exact capture, catalog unit, and authority snapshots validated by the sink.</param>
    void Handle(LiveCaptureContext context);
}
