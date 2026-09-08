namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Contains the archive produced by an Adapter+Host build.</summary>
/// <param name="ArchivePath">The path to the packaged Vortex-ready archive.</param>
public sealed record AdapterHostBuildResult(string ArchivePath);
