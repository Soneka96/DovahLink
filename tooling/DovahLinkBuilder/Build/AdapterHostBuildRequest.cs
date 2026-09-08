namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Describes the repository to build the production Adapter and Host for.</summary>
/// <param name="RepositoryRoot">The DovahLink repository root.</param>
public sealed record AdapterHostBuildRequest(string RepositoryRoot);
