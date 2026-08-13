using DovahLink.BridgeBuilder.Build;
using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Ui;

/// <summary>Displays bridge build controls, progress, and results.</summary>
public sealed class MainForm : Form
{
    /// <summary>The repository being built.</summary>
    private readonly string repositoryRoot;

    /// <summary>Coordinates bridge compilation and packaging.</summary>
    private readonly BridgeBuildCoordinator coordinator;

    /// <summary>Tracks the build state shown by the form.</summary>
    private readonly BuildViewModel viewModel = new();

    /// <summary>Cancels a build when the form closes.</summary>
    private readonly CancellationTokenSource buildCancellation = new();

    /// <summary>Starts a beta build.</summary>
    private readonly Button betaButton = new() { Text = "Build Beta", AutoSize = true };

    /// <summary>Starts a release build.</summary>
    private readonly Button releaseButton = new() { Text = "Build Release", AutoSize = true };

    /// <summary>Opens the output directory after a successful build.</summary>
    private readonly Button openOutputButton = new() { Text = "Open Output Folder", AutoSize = true, Enabled = false };

    /// <summary>Displays the current build status.</summary>
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Ready" };

    /// <summary>Displays build command output.</summary>
    private readonly TextBox outputLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9),
    };

    /// <summary>
    /// Initializes the Bridge Builder window for the specified repository.
    /// </summary>
    /// <param name="repositoryRoot">The root directory of the repository to build.</param>
    public MainForm(string repositoryRoot)
    {
        this.repositoryRoot = repositoryRoot;
        coordinator = new BridgeBuildCoordinator(new ProcessCommandRunner(), VisualStudioToolchainLocator.Find);

        Text = "DovahLink Bridge Builder";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 460);
        Size = new Size(900, 600);
        FormClosing += (_, _) => buildCancellation.Cancel();

        betaButton.Click += async (_, _) => await RunBuildAsync(PackageChannel.Beta);
        releaseButton.Click += async (_, _) => await RunBuildAsync(PackageChannel.Release);
        openOutputButton.Click += (_, _) => OpenOutputFolder();

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(betaButton);
        buttons.Controls.Add(releaseButton);
        buttons.Controls.Add(openOutputButton);

        var statusPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        statusPanel.Controls.Add(statusLabel);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(statusPanel, 0, 1);
        layout.Controls.Add(outputLog, 0, 2);
        Controls.Add(layout);
    }

    /// <summary>
    /// Runs a Bridge package build for the specified channel and updates the build state and output log.
    /// </summary>
    /// <param name="channel">The package channel to build.</param>
    private async Task RunBuildAsync(PackageChannel channel)
    {
        if (!viewModel.TryBeginBuild())
        {
            return;
        }

        UpdateControls();
        AppendOutput($"> Starting {channel} build");
        try
        {
            BridgeBuildResult result = await coordinator.BuildAsync(
                new BridgeBuildRequest(repositoryRoot, channel),
                AppendOutput,
                buildCancellation.Token);
            viewModel.Complete(result.ArchivePath);
            AppendOutput($"> Finished: {result.ArchivePath}");
        }
        catch (OperationCanceledException) when (buildCancellation.IsCancellationRequested)
        {
            if (!IsDisposed && !Disposing)
            {
                viewModel.Fail("Build canceled");
                AppendOutput("> Build canceled");
            }
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing)
            {
                viewModel.Fail(exception.Message);
                AppendOutput($"> ERROR: {exception.Message}");
                MessageBox.Show(this, exception.Message, "Bridge build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        if (!IsDisposed && !Disposing)
        {
            UpdateControls();
        }
    }

    /// <summary>
    /// Updates build controls and status text to reflect the current build state.
    /// </summary>
    private void UpdateControls()
    {
        bool canBuild = viewModel.State.CanBuild;
        betaButton.Enabled = canBuild;
        releaseButton.Enabled = canBuild;
        openOutputButton.Enabled = viewModel.State.ArchivePath is not null;
        statusLabel.Text = viewModel.State.Message;
    }

    /// <summary>
    /// Opens the build output folder in the operating system's file manager.
    /// </summary>
    private void OpenOutputFolder()
    {
        string outputRoot = Path.Combine(repositoryRoot, "tooling", "out");
        Directory.CreateDirectory(outputRoot);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = outputRoot,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Appends a line to the build output log when the form is available.
    /// </summary>
    /// <param name="line">The output line to append.</param>
    private void AppendOutput(string line)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action<string>(AppendOutput), line);
            }
            catch (InvalidOperationException)
            {
                // The form can close between the lifecycle check and BeginInvoke.
            }

            return;
        }

        outputLog.AppendText(line + Environment.NewLine);
    }
}
