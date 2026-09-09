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
    }
}
