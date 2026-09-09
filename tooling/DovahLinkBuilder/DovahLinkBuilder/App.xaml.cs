using System.Windows;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder;

/// <summary>The DovahLink Builder WPF application; composes and shows the main window.</summary>
public partial class App : Application
{
    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(new MainWindowViewModel()).Show();
    }
}
