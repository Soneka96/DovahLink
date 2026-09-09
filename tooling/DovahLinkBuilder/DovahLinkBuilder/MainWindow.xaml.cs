using System.ComponentModel;
using System.Windows;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder;

/// <summary>The Builder's main window: a navigation rail and the currently selected page.</summary>
public partial class MainWindow : Window
{
    /// <summary>Initializes the window over the supplied navigation ViewModel.</summary>
    /// <param name="viewModel">Owns navigation between the Build, Environment, and Settings pages.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// Blocks closing while a build is running until the user confirms, then cancels the build and
    /// waits for it to actually finish terminating before letting the window close -- regardless of
    /// which page is currently displayed, since a build can keep running in the background while the
    /// user has navigated away from the Build page.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">Carries the cancel flag this handler sets to block the close.</param>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.BuildPage.IsBuilding)
        {
            return;
        }

        e.Cancel = true;

        MessageBoxResult result = MessageBox.Show(
            "A build is currently running. Closing now will terminate it. Close anyway?",
            "Build in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.BuildPage.CancelCommand.Execute(null);
        if (viewModel.BuildPage.RunningBuildTask is { } buildTask)
        {
            await buildTask;
        }

        Close();
    }
}
