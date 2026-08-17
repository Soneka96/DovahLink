namespace DovahLink.BridgeBuilder.Packaging;

/// <summary>Maps one build output file to its path inside the package.</summary>
/// <param name="SourceName">The file name in the build output directory.</param>
/// <param name="ArchivePath">The destination path inside the archive.</param>
public sealed record ArtifactFile(string SourceName, string ArchivePath);

/// <summary>Describes the files and name of one bridge package.</summary>
/// <param name="Version">The bridge version being packaged.</param>
/// <param name="Channel">The package channel.</param>
/// <param name="ArchiveName">The generated archive file name.</param>
/// <param name="Files">The runtime files included in the archive.</param>
public sealed record ArtifactPlan(
    BridgeVersion Version,
    PackageChannel Channel,
    string ArchiveName,
    IReadOnlyList<ArtifactFile> Files)
{
    /// <summary>
    /// Creates a packaging plan for a bridge version and package channel.
    /// </summary>
    /// <param name="version">The bridge version to package.</param>
    /// <param name="channel">The package channel for the artifact.</param>
    /// <returns>The archive name and runtime files with their archive paths.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="channel"/> is not a defined package channel.</exception>
    public static ArtifactPlan Create(BridgeVersion version, PackageChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown package channel.");
        }

        string channelSuffix = channel == PackageChannel.Beta ? "-beta" : string.Empty;
        string archiveName = $"DovahLink-Bridge-{version}{channelSuffix}.zip";
        string pluginsDirectory = Path.Combine("Data", "SKSE", "Plugins");

        string[] runtimeFiles =
        [
            "dovahlink_bridge_plugin.dll",
            "boost_json-vc143-mt-x64-1_91.dll",
            "fmt.dll",
            "spdlog.dll",
        ];

        ArtifactFile[] files =
        [
            .. runtimeFiles.Select(file => new ArtifactFile(file, Path.Combine(pluginsDirectory, file))),
            new ArtifactFile("DovahLinkAdmin.pex", Path.Combine("Data", "Scripts", "DovahLinkAdmin.pex")),
            new ArtifactFile("dovahlink.yaml", Path.Combine("Data", "SKSE", "CustomConsole", "dovahlink.yaml")),
        ];

        return new ArtifactPlan(version, channel, archiveName, files);
    }
}
