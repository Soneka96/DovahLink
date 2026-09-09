using System.ComponentModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Owns navigation between the Builder's three top-level pages, preserving each page's state across navigation.</summary>
public interface IMainWindowViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the Build page's ViewModel, kept alive for the application's lifetime. Public so the main
    /// window's close handler can check for an in-flight build regardless of the currently displayed page.
    /// </summary>
    IBuildPageViewModel BuildPage { get; }

    /// <summary>Gets the currently displayed page's ViewModel.</summary>
    object CurrentPage { get; }

    /// <summary>Gets whether the Build page is currently displayed, for highlighting its nav item.</summary>
    bool IsBuildPageActive { get; }

    /// <summary>Gets whether the Environment page is currently displayed, for highlighting its nav item.</summary>
    bool IsEnvironmentPageActive { get; }

    /// <summary>Gets whether the Settings page is currently displayed, for highlighting its nav item.</summary>
    bool IsSettingsPageActive { get; }

    /// <summary>Gets the command that navigates to the Build page.</summary>
    RelayCommand NavigateToBuildCommand { get; }

    /// <summary>Gets the command that navigates to the Environment page.</summary>
    RelayCommand NavigateToEnvironmentCommand { get; }

    /// <summary>Gets the command that navigates to the Settings page.</summary>
    RelayCommand NavigateToSettingsCommand { get; }
}

/// <inheritdoc cref="IMainWindowViewModel"/>
public sealed class MainWindowViewModel : ObservableObject, IMainWindowViewModel
{
    /// <summary>The backing field for <see cref="CurrentPage"/>.</summary>
    private object currentPage;

    /// <summary>Initializes navigation over the already-constructed page ViewModels, starting on the Build page.</summary>
    /// <param name="buildPage">The Build page's ViewModel, kept alive for the application's lifetime.</param>
    /// <param name="environmentPage">The Environment page's ViewModel, kept alive for the application's lifetime.</param>
    /// <param name="settingsPage">The Settings page's ViewModel, kept alive for the application's lifetime.</param>
    public MainWindowViewModel(
        IBuildPageViewModel buildPage,
        IEnvironmentPageViewModel environmentPage,
        ISettingsPageViewModel settingsPage)
    {
        BuildPage = buildPage;
        EnvironmentPage = environmentPage;
        SettingsPage = settingsPage;
        currentPage = BuildPage;
        NavigateToBuildCommand = new RelayCommand(() => CurrentPage = BuildPage);
        NavigateToEnvironmentCommand = new RelayCommand(() => CurrentPage = EnvironmentPage);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentPage = SettingsPage);
    }

    /// <inheritdoc/>
    public IBuildPageViewModel BuildPage { get; }

    /// <summary>Gets the Environment page's ViewModel, kept alive for the application's lifetime.</summary>
    private IEnvironmentPageViewModel EnvironmentPage { get; }

    /// <summary>Gets the Settings page's ViewModel, kept alive for the application's lifetime.</summary>
    private ISettingsPageViewModel SettingsPage { get; }

    /// <inheritdoc/>
    public object CurrentPage
    {
        get => currentPage;
        private set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(IsBuildPageActive));
                OnPropertyChanged(nameof(IsEnvironmentPageActive));
                OnPropertyChanged(nameof(IsSettingsPageActive));
            }
        }
    }

    /// <inheritdoc/>
    public bool IsBuildPageActive => ReferenceEquals(CurrentPage, BuildPage);

    /// <inheritdoc/>
    public bool IsEnvironmentPageActive => ReferenceEquals(CurrentPage, EnvironmentPage);

    /// <inheritdoc/>
    public bool IsSettingsPageActive => ReferenceEquals(CurrentPage, SettingsPage);

    /// <inheritdoc/>
    public RelayCommand NavigateToBuildCommand { get; }

    /// <inheritdoc/>
    public RelayCommand NavigateToEnvironmentCommand { get; }

    /// <inheritdoc/>
    public RelayCommand NavigateToSettingsCommand { get; }
}
