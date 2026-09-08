using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Ui;

// Initializes the WinForms application and opens the builder window.
ApplicationConfiguration.Initialize();

try
{
    string repositoryRoot = RepositoryRootLocator.Find(AppContext.BaseDirectory);
    IAdapterHostBuildCoordinator coordinator = new AdapterHostBuildCoordinator(
        new ProcessCommandRunner(),
        VisualStudioToolchainLocator.Find,
        PapyrusToolchainLocator.Find);
    Application.Run(new MainForm(repositoryRoot, coordinator));
}
catch (Exception exception)
{
    Environment.ExitCode = 1;
    MessageBox.Show(
        exception.Message,
        "DovahLink Builder",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
