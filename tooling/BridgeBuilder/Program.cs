using DovahLink.BridgeBuilder.Ui;

ApplicationConfiguration.Initialize();

try
{
    string repositoryRoot = RepositoryRootLocator.Find(AppContext.BaseDirectory);
    Application.Run(new MainForm(repositoryRoot));
}
catch (Exception exception)
{
    Environment.ExitCode = 1;
    MessageBox.Show(
        exception.Message,
        "DovahLink Bridge Builder",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
