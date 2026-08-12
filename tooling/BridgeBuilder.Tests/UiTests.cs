using DovahLink.BridgeBuilder.Ui;

namespace DovahLink.BridgeBuilder.Tests;

public sealed class UiTests
{
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

    [Fact]
    public void RejectsAPathOutsideARepository()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() => RepositoryRootLocator.Find(temporaryDirectory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBridgeBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
