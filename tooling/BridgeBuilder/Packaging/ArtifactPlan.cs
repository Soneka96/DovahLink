namespace DovahLink.BridgeBuilder.Packaging;

public sealed record ArtifactFile(string SourceName, string ArchivePath);

public sealed record ArtifactPlan(
    BridgeVersion Version,
    PackageChannel Channel,
    string ArchiveName,
    IReadOnlyList<ArtifactFile> Files)
{
    public static ArtifactPlan Create(BridgeVersion version, PackageChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown package channel.");
        }

        string channelSuffix = channel == PackageChannel.Beta ? "-beta" : string.Empty;
        string archiveName = $"DovahLink-Bridge-{version}{channelSuffix}.zip";
        string archiveDirectory = Path.Combine("Data", "SKSE", "Plugins");

        string[] runtimeFiles =
        [
            "dovahlink_bridge_plugin.dll",
            "boost_json-vc143-mt-x64-1_91.dll",
            "fmt.dll",
            "spdlog.dll",
        ];

        return new ArtifactPlan(
            version,
            channel,
            archiveName,
            runtimeFiles.Select(file => new ArtifactFile(file, Path.Combine(archiveDirectory, file))).ToArray());
    }
}
