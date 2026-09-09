using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="MainWindowViewModel"/>'s navigation behavior.</summary>
public sealed class MainWindowViewModelTests
{
    /// <summary>Starts with the Build page selected.</summary>
    [Fact]
    public void StartsOnTheBuildPage()
    {
        var viewModel = new MainWindowViewModel();

        Assert.IsType<BuildPageViewModel>(viewModel.CurrentPage);
    }

    /// <summary>Navigates to each page's ViewModel when its command executes.</summary>
    [Fact]
    public void NavigationCommandsSwitchTheCurrentPage()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateToEnvironmentCommand.Execute(null);
        Assert.IsType<EnvironmentPageViewModel>(viewModel.CurrentPage);

        viewModel.NavigateToSettingsCommand.Execute(null);
        Assert.IsType<SettingsPageViewModel>(viewModel.CurrentPage);

        viewModel.NavigateToBuildCommand.Execute(null);
        Assert.IsType<BuildPageViewModel>(viewModel.CurrentPage);
    }

    /// <summary>Preserves each page's own ViewModel instance across repeated navigation, so page state survives switching away and back.</summary>
    [Fact]
    public void NavigatingBackToAPageReturnsTheSameViewModelInstance()
    {
        var viewModel = new MainWindowViewModel();
        var initialBuildPage = viewModel.CurrentPage;

        viewModel.NavigateToSettingsCommand.Execute(null);
        viewModel.NavigateToBuildCommand.Execute(null);

        Assert.Same(initialBuildPage, viewModel.CurrentPage);
    }

    /// <summary>Raises <c>PropertyChanged</c> for <see cref="MainWindowViewModel.CurrentPage"/> on navigation.</summary>
    [Fact]
    public void NavigationRaisesPropertyChangedForCurrentPage()
    {
        var viewModel = new MainWindowViewModel();
        var raisedPropertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        viewModel.NavigateToEnvironmentCommand.Execute(null);

        Assert.Equal([nameof(MainWindowViewModel.CurrentPage)], raisedPropertyNames);
    }

    /// <summary>Does not raise <c>PropertyChanged</c> when navigating to the page that is already current.</summary>
    [Fact]
    public void NavigatingToTheAlreadyCurrentPageDoesNotRaisePropertyChanged()
    {
        var viewModel = new MainWindowViewModel();
        var raisedPropertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        viewModel.NavigateToBuildCommand.Execute(null);

        Assert.Empty(raisedPropertyNames);
    }
}
