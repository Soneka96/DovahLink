namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Parses `##stage &lt;name&gt; &lt;status&gt;` progress markers emitted by `tooling/package_adapter_host.py`.</summary>
public static class BuildStageProgressParser
{
    /// <summary>The line prefix a stage progress marker starts with.</summary>
    private const string MarkerPrefix = "##stage ";

    /// <summary>Maps each marker's stage name to the <see cref="Build.BuildStage"/> it reports.</summary>
    private static readonly IReadOnlyDictionary<string, BuildStage> StagesByMarkerName = new Dictionary<string, BuildStage>(StringComparer.Ordinal)
    {
        ["host_publish"] = BuildStage.PublishHost,
        ["package_assembly"] = BuildStage.AssemblePackage,
        ["package_validation"] = BuildStage.ValidatePackage,
        ["archive"] = BuildStage.CreateZip,
    };

    /// <summary>Parses one line of packaging output as a stage progress marker, when it is one.</summary>
    /// <param name="line">A line of the packaging command's standard output.</param>
    /// <returns>
    /// The parsed stage and status, or <see langword="null"/> when <paramref name="line"/> is not a
    /// recognized marker. A marker's status is always <see cref="BuildStageStatus.Running"/> (for
    /// `start`) or <see cref="BuildStageStatus.Succeeded"/> (for `done`) -- the packaging script
    /// never emits a `done` marker for a stage that failed, so a failed stage is detected by its
    /// absence, not parsed directly from a line.
    /// </returns>
    public static (BuildStage Stage, BuildStageStatus Status)? TryParse(string line)
    {
        if (!line.StartsWith(MarkerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = line[MarkerPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !StagesByMarkerName.TryGetValue(parts[0], out BuildStage stage))
        {
            return null;
        }

        return parts[1] switch
        {
            "start" => (stage, BuildStageStatus.Running),
            "done" => (stage, BuildStageStatus.Succeeded),
            _ => null,
        };
    }
}
