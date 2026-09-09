using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies repository root discovery.</summary>
public sealed class RepositoryRootLocatorTests
{
    /// <summary>Finds the repository root from a child directory.</summary>
    [Fact]
    public void FindsTheRepositoryFromAChildDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        string childDirectory = Path.Combine(repositoryRoot, "tooling", "DovahLinkBuilder", "bin");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");

        Assert.Equal(repositoryRoot, RepositoryRootLocator.Find(childDirectory));
    }

    /// <summary>Rejects a path that is outside a DovahLink repository.</summary>
    [Fact]
    public void RejectsAPathOutsideARepository()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() => RepositoryRootLocator.Find(temporaryDirectory.Path));
    }

}
