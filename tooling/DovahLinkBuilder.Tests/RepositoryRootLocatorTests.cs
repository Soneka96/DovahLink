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

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a unique temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the temporary directory path.</summary>
        public string Path { get; }

        /// <summary>Removes the temporary directory when the test completes.</summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
