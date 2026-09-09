namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the repository to build the production Adapter and Host for.</summary>
/// <param name="RepositoryRoot">The DovahLink repository root.</param>
/// <param name="Profile">The build profile to target.</param>
/// <param name="OutputRootOverride">The published Host, assembled package, and ZIP output root to use instead of <see cref="BuildProfileExtensions.ToOutputRoot"/>'s default, or <see langword="null"/> to use that default.</param>
public sealed record AdapterHostBuildRequest(string RepositoryRoot, BuildProfile Profile = BuildProfile.Release, string? OutputRootOverride = null);
