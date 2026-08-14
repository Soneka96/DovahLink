using DovahLink.BridgeBuilder.Ui;

namespace DovahLink.BridgeBuilder.Tests;

/// <summary>Verifies builder state transitions and repository discovery.</summary>
public sealed class UiTests
{
    /// <summary>Starts ready and prevents concurrent builds.</summary>
    [Fact]
    public void BuildViewModelStartsReadyAndPreventsConcurrentBuilds()
    {
        var viewModel = new BuildViewModel();

        Assert.Equal(BuildUiStatus.Ready, viewModel.State.Status);
        Assert.True(viewModel.TryBeginBuild());
        Assert.False(viewModel.TryBeginBuild());
        Assert.Equal(BuildUiStatus.Building, viewModel.State.Status);
        Assert.False(viewModel.State.CanBuild);
    }

    /// <summary>Exposes the archive path after a successful build.</summary>
    [Fact]
    public void BuildViewModelExposesTheSuccessfulArchive()
    {
        var viewModel = new BuildViewModel();
        viewModel.TryBeginBuild();

        viewModel.Complete(@"C:\DovahLink\tooling\out\DovahLink-Bridge-0.1.0-beta.zip");

        Assert.Equal(BuildUiStatus.Succeeded, viewModel.State.Status);
        Assert.Equal("Build complete", viewModel.State.Message);
        Assert.NotNull(viewModel.State.ArchivePath);
        Assert.True(viewModel.State.CanBuild);
    }

    /// <summary>Exposes failure state and clears a previous archive path.</summary>
    [Fact]
    public void BuildViewModelExposesFailureAndClearsTheArchive()
    {
        var viewModel = new BuildViewModel();
        viewModel.TryBeginBuild();

        viewModel.Fail("Build failed");

        Assert.Equal(BuildUiStatus.Failed, viewModel.State.Status);
        Assert.Equal("Build failed", viewModel.State.Message);
        Assert.Null(viewModel.State.ArchivePath);
        Assert.True(viewModel.State.CanBuild);
    }

    /// <summary>Finds the repository root from a child directory.</summary>
    [Fact]
    public void FindsTheRepositoryFromAChildDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        string childDirectory = Path.Combine(repositoryRoot, "tooling", "BridgeBuilder", "bin");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "bridge"));
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, "bridge", "vcpkg.json"), "{}");

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
                "DovahLinkBridgeBuilderTests",
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
