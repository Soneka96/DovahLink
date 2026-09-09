namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the repository to build the production Adapter and Host for.</summary>
/// <param name="RepositoryRoot">The DovahLink repository root.</param>
/// <param name="Profile">The build profile to target.</param>
public sealed record AdapterHostBuildRequest(string RepositoryRoot, BuildProfile Profile = BuildProfile.Release);
