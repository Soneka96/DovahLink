using DovahLink.DovahLinkBuilder;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the Builder application's startup repository-root resolution.</summary>
public sealed class AppTests
{
    /// <summary>Finds the repository root from a child directory.</summary>
    [Fact]
    public void TryFindRepositoryRootFindsTheRepositoryFromAChildDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        string childDirectory = Path.Combine(repositoryRoot, "tooling", "DovahLinkBuilder", "bin");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");

        Assert.Equal(repositoryRoot, App.TryFindRepositoryRoot(childDirectory));
    }

    /// <summary>Returns <see langword="null"/>, rather than throwing, for a path outside a DovahLink repository.</summary>
    [Fact]
    public void TryFindRepositoryRootReturnsNullForAPathOutsideARepository()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Null(App.TryFindRepositoryRoot(temporaryDirectory.Path));
    }

    /// <summary>Prefers the configured override over a successfully discovered root.</summary>
    [Fact]
    public void ResolveRepositoryRootsPrefersTheConfiguredOverride()
    {
        (string activeRepositoryRoot, string autoDetectedRepositoryRoot) =
            App.ResolveRepositoryRoots(configuredOverride: @"C:\override", discoveredRoot: @"C:\discovered");

        Assert.Equal(@"C:\override", activeRepositoryRoot);
        Assert.Equal(@"C:\discovered", autoDetectedRepositoryRoot);
    }

    /// <summary>
    /// Starts on a configured override alone, without requiring discovery to have succeeded -- the
    /// exact startup crash this method exists to prevent.
    /// </summary>
    [Fact]
    public void ResolveRepositoryRootsUsesTheConfiguredOverrideWhenDiscoveryFails()
    {
        (string activeRepositoryRoot, string autoDetectedRepositoryRoot) =
            App.ResolveRepositoryRoots(configuredOverride: @"C:\override", discoveredRoot: null);

        Assert.Equal(@"C:\override", activeRepositoryRoot);
        Assert.Equal(@"C:\override", autoDetectedRepositoryRoot);
    }

    /// <summary>Falls back to the discovered root when no override is configured.</summary>
    [Fact]
    public void ResolveRepositoryRootsFallsBackToTheDiscoveredRootWhenNoOverrideIsConfigured()
    {
        (string activeRepositoryRoot, string autoDetectedRepositoryRoot) =
            App.ResolveRepositoryRoots(configuredOverride: null, discoveredRoot: @"C:\discovered");

        Assert.Equal(@"C:\discovered", activeRepositoryRoot);
        Assert.Equal(@"C:\discovered", autoDetectedRepositoryRoot);
    }

    /// <summary>Throws when neither a configured override nor a discovered root is available.</summary>
    [Fact]
    public void ResolveRepositoryRootsThrowsWhenNeitherAnOverrideNorADiscoveredRootIsAvailable()
    {
        Assert.Throws<InvalidOperationException>(() => App.ResolveRepositoryRoots(configuredOverride: null, discoveredRoot: null));
    }
}
