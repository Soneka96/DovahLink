using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder;

/// <summary>The Builder's main window: a navigation rail and the currently selected page.</summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Resolves to <see langword="true"/> once the user has chosen to cancel the running build and
    /// close, or <see langword="false"/> once they have chosen to keep building; <see langword="null"/>
    /// while the in-app close-confirmation dialog is not showing.
    /// </summary>
    private TaskCompletionSource<bool>? closeConfirmation;

    /// <summary>Initializes the window over the supplied navigation ViewModel.</summary>
    /// <param name="viewModel">Owns navigation between the Build, Environment, and Settings pages.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// Blocks closing while a build is running until the user confirms through the in-app close
    /// dialog, then cancels the build and waits for it to actually finish terminating before letting
    /// the window close -- regardless of which page is currently displayed, since a build can keep
    /// running in the background while the user has navigated away from the Build page. While the
    /// dialog is showing, keyboard focus moves onto it (kept there by <c>CloseOverlay</c>'s
    /// <c>KeyboardNavigation.TabNavigation="Cycle"</c> in the markup) and is restored to whatever had
    /// focus beforehand once the dialog closes.
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

        IInputElement? previouslyFocused = Keyboard.FocusedElement;
        closeConfirmation = new TaskCompletionSource<bool>();
        CloseOverlay.Visibility = Visibility.Visible;
        KeepBuildingButton.Focus();
        bool shouldClose = await closeConfirmation.Task;
        CloseOverlay.Visibility = Visibility.Collapsed;
        previouslyFocused?.Focus();
        closeConfirmation = null;
        if (!shouldClose)
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

    /// <summary>Dismisses the close-confirmation dialog without closing the window.</summary>
    private void OnKeepBuildingClick(object sender, RoutedEventArgs e)
    {
        closeConfirmation?.TrySetResult(false);
    }

    /// <summary>Confirms cancelling the running build and closing the window.</summary>
    private void OnCancelBuildAndCloseClick(object sender, RoutedEventArgs e)
    {
        closeConfirmation?.TrySetResult(true);
    }
}
