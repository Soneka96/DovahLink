namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Owns navigation between the Builder's three top-level pages, preserving each page's state across navigation.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>The backing field for <see cref="CurrentPage"/>.</summary>
    private object currentPage;

    /// <summary>Initializes navigation over the already-constructed page ViewModels, starting on the Build page.</summary>
    /// <param name="buildPage">The Build page's ViewModel, kept alive for the application's lifetime.</param>
    /// <param name="environmentPage">The Environment page's ViewModel, kept alive for the application's lifetime.</param>
    /// <param name="settingsPage">The Settings page's ViewModel, kept alive for the application's lifetime.</param>
    public MainWindowViewModel(
        BuildPageViewModel buildPage,
        EnvironmentPageViewModel environmentPage,
        SettingsPageViewModel settingsPage)
    {
        BuildPage = buildPage;
        EnvironmentPage = environmentPage;
        SettingsPage = settingsPage;
        currentPage = BuildPage;
        NavigateToBuildCommand = new RelayCommand(() => CurrentPage = BuildPage);
        NavigateToEnvironmentCommand = new RelayCommand(() => CurrentPage = EnvironmentPage);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentPage = SettingsPage);
    }

    /// <summary>
    /// Gets the Build page's ViewModel, kept alive for the application's lifetime. Public so the main
    /// window's close handler can check for an in-flight build regardless of the currently displayed page.
    /// </summary>
    public BuildPageViewModel BuildPage { get; }

    /// <summary>Gets the Environment page's ViewModel, kept alive for the application's lifetime.</summary>
    private EnvironmentPageViewModel EnvironmentPage { get; }

    /// <summary>Gets the Settings page's ViewModel, kept alive for the application's lifetime.</summary>
    private SettingsPageViewModel SettingsPage { get; }

    /// <summary>Gets the currently displayed page's ViewModel.</summary>
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

    /// <summary>Gets whether the Build page is currently displayed, for highlighting its nav item.</summary>
    public bool IsBuildPageActive => ReferenceEquals(CurrentPage, BuildPage);

    /// <summary>Gets whether the Environment page is currently displayed, for highlighting its nav item.</summary>
    public bool IsEnvironmentPageActive => ReferenceEquals(CurrentPage, EnvironmentPage);

    /// <summary>Gets whether the Settings page is currently displayed, for highlighting its nav item.</summary>
    public bool IsSettingsPageActive => ReferenceEquals(CurrentPage, SettingsPage);

    /// <summary>Gets the command that navigates to the Build page.</summary>
    public RelayCommand NavigateToBuildCommand { get; }

    /// <summary>Gets the command that navigates to the Environment page.</summary>
    public RelayCommand NavigateToEnvironmentCommand { get; }

    /// <summary>Gets the command that navigates to the Settings page.</summary>
    public RelayCommand NavigateToSettingsCommand { get; }
}
