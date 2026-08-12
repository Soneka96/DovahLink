namespace DovahLink.BridgeBuilder.Ui;

public enum BuildUiStatus
{
    Ready,
    Building,
    Succeeded,
    Failed,
}

public sealed record BuildUiState(
    BuildUiStatus Status,
    string Message,
    string? ArchivePath)
{
    public bool IsBuilding => Status == BuildUiStatus.Building;
    public bool CanBuild => !IsBuilding;
}

public sealed class BuildViewModel
{
    public BuildUiState State { get; private set; } = new(BuildUiStatus.Ready, "Ready", null);

    public bool TryBeginBuild()
    {
        if (!State.CanBuild)
        {
            return false;
        }

        State = new BuildUiState(BuildUiStatus.Building, "Building the bridge...", null);
        return true;
    }

    public void Complete(string archivePath)
    {
        State = new BuildUiState(BuildUiStatus.Succeeded, "Build complete", archivePath);
    }

    public void Fail(string message)
    {
        State = new BuildUiState(BuildUiStatus.Failed, message, null);
    }
}
