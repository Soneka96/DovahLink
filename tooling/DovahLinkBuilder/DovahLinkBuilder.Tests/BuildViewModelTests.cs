using DovahLink.DovahLinkBuilder;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies builder state transitions.</summary>
public sealed class BuildViewModelTests
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

        viewModel.Complete(@"C:\DovahLink\tooling\out\DovahLink-Adapter-0.1.0.zip");

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
}
