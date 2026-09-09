using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder;

/// <summary>The Builder's main window: a navigation rail and the currently selected page.</summary>
public partial class MainWindow : Window
{
    /// <summary>Persists the window's bounds when it closes, for <see cref="OnClosed"/>.</summary>
    private readonly ISettingsStore settingsStore;

    /// <summary>
    /// Resolves to <see langword="true"/> once the user has chosen to cancel the running build and
    /// close, or <see langword="false"/> once they have chosen to keep building; <see langword="null"/>
    /// while the in-app close-confirmation dialog is not showing.
    /// </summary>
    private TaskCompletionSource<bool>? closeConfirmation;

    /// <summary>Whether the window was maximized, captured at the very start of <see cref="OnClosing"/>; see <see cref="OnClosed"/> for why.</summary>
    private bool wasMaximizedOnClosing;

    /// <summary>The window's normal (non-maximized) bounds, captured at the very start of <see cref="OnClosing"/>; see <see cref="OnClosed"/> for why.</summary>
    private Rect boundsOnClosing;

    /// <summary>Initializes the window over the supplied navigation ViewModel.</summary>
    /// <param name="viewModel">Owns navigation between the Build, Environment, and Settings pages.</param>
    /// <param name="settingsStore">Persists the window's bounds when it closes.</param>
    public MainWindow(MainWindowViewModel viewModel, ISettingsStore settingsStore)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.settingsStore = settingsStore;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>
    /// Blocks closing while a build is running until the user confirms through the in-app close
    /// dialog, then cancels the build and waits for it to actually finish terminating before letting
    /// the window close -- regardless of which page is currently displayed, since a build can keep
    /// running in the background while the user has navigated away from the Build page. While the
    /// dialog is showing, keyboard focus moves onto it (kept there by <c>CloseOverlay</c>'s
    /// <c>KeyboardNavigation.TabNavigation="Cycle"</c> in the markup) and is restored to whatever had
    /// focus beforehand once the dialog closes. Captures <see cref="wasMaximizedOnClosing"/> and
    /// <see cref="boundsOnClosing"/> as its very first statement, before anything else runs: closing a
    /// maximized window makes Windows restore it as part of the close sequence, so <see cref="WindowState"/>
    /// already reads back as <see cref="WindowState.Normal"/> by the time <see cref="OnClosed"/> fires.
    /// Blocks a re-entrant call (for example a second Alt+F4 while the dialog is already showing)
    /// without replacing <see cref="closeConfirmation"/>: doing so would strand the first call's await
    /// on a <see cref="TaskCompletionSource{TResult}"/> nothing can ever complete again, since
    /// <see cref="OnKeepBuildingClick"/> and <see cref="OnCancelBuildAndCloseClick"/> only ever resolve
    /// whichever instance this field currently holds.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">Carries the cancel flag this handler sets to block the close.</param>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        wasMaximizedOnClosing = WindowState == WindowState.Maximized;
        boundsOnClosing = wasMaximizedOnClosing ? RestoreBounds : new Rect(Left, Top, Width, Height);

        if (closeConfirmation is not null)
        {
            e.Cancel = true;
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.BuildPage.IsBuilding)
        {
            return;
        }

        e.Cancel = true;

        IInputElement? previouslyFocused = Keyboard.FocusedElement;
        closeConfirmation = new TaskCompletionSource<bool>();
        bool shouldClose;
        try
        {
            CloseOverlay.Visibility = Visibility.Visible;
            KeepBuildingButton.Focus();
            shouldClose = await closeConfirmation.Task;
        }
        finally
        {
            // Guarantees the overlay is hidden, focus is restored, and closeConfirmation is cleared
            // even if something above throws -- without this, a failure here would leave
            // closeConfirmation non-null forever, permanently blocking every future close attempt
            // through the re-entrancy guard above.
            CloseOverlay.Visibility = Visibility.Collapsed;
            previouslyFocused?.Focus();
            closeConfirmation = null;
        }

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

    /// <summary>
    /// Saves the bounds <see cref="OnClosing"/> captured once the window has actually closed --
    /// <see cref="OnClosing"/> may cancel and re-run through the build-confirmation flow first, so this
    /// uses <see cref="Closed"/> instead to save exactly once, only once closing is final. Uses the
    /// snapshot from <see cref="OnClosing"/> rather than reading <see cref="Window.WindowState"/> again
    /// here, since Windows has already restored a maximized window by this point as part of its own
    /// close sequence.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused event data.</param>
    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            settingsStore.Save(settingsStore.Load() with
            {
                WindowLeft = boundsOnClosing.Left,
                WindowTop = boundsOnClosing.Top,
                WindowWidth = boundsOnClosing.Width,
                WindowHeight = boundsOnClosing.Height,
                WindowIsMaximized = wasMaximizedOnClosing,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saving the window's last geometry is a convenience for next launch; a failure here
            // must never prevent the window -- and the application shutdown this event is part of
            // -- from closing.
        }
    }
}
