namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Owns navigation between the Builder's three top-level pages, preserving each page's state across navigation.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>The backing field for <see cref="CurrentPage"/>.</summary>
    private object currentPage;

    /// <summary>Initializes navigation with the Build page selected.</summary>
    public MainWindowViewModel()
    {
        BuildPage = new BuildPageViewModel();
        EnvironmentPage = new EnvironmentPageViewModel();
        SettingsPage = new SettingsPageViewModel();
        currentPage = BuildPage;
        NavigateToBuildCommand = new RelayCommand(() => CurrentPage = BuildPage);
        NavigateToEnvironmentCommand = new RelayCommand(() => CurrentPage = EnvironmentPage);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentPage = SettingsPage);
    }

    /// <summary>Gets the Build page's ViewModel, kept alive for the application's lifetime.</summary>
    private BuildPageViewModel BuildPage { get; }

    /// <summary>Gets the Environment page's ViewModel, kept alive for the application's lifetime.</summary>
    private EnvironmentPageViewModel EnvironmentPage { get; }

    /// <summary>Gets the Settings page's ViewModel, kept alive for the application's lifetime.</summary>
    private SettingsPageViewModel SettingsPage { get; }

    /// <summary>Gets the currently displayed page's ViewModel.</summary>
    public object CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    /// <summary>Gets the command that navigates to the Build page.</summary>
    public RelayCommand NavigateToBuildCommand { get; }

    /// <summary>Gets the command that navigates to the Environment page.</summary>
    public RelayCommand NavigateToEnvironmentCommand { get; }

    /// <summary>Gets the command that navigates to the Settings page.</summary>
    public RelayCommand NavigateToSettingsCommand { get; }
}
