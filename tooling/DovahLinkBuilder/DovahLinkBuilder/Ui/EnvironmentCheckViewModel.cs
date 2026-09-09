using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Represents one required build tool's preflight check, with a curated remediation action label.</summary>
public sealed class EnvironmentCheckViewModel : ObservableObject
{
    /// <summary>Initializes a check row over the given preflight result.</summary>
    /// <param name="result">The preflight result this row reports.</param>
    public EnvironmentCheckViewModel(ToolchainCheckResult result)
    {
        Result = result;
    }

    /// <summary>Gets the underlying preflight result this row reports.</summary>
    public ToolchainCheckResult Result { get; }

    /// <summary>Gets the human-readable tool name.</summary>
    public string ToolName => Result.ToolName;

    /// <summary>Gets whether the tool is available.</summary>
    public ToolchainAvailability Availability => Result.Availability;

    /// <summary>Gets the resolved location of the tool when found; otherwise <see langword="null"/>.</summary>
    public string? Detail => Result.Detail;

    /// <summary>Gets the raw remediation detail explaining what is missing or invalid, when not found; otherwise <see langword="null"/>.</summary>
    public string? RemediationHint => Result.RemediationHint;

    /// <summary>Gets whether this tool was located and validated.</summary>
    public bool IsAvailable => Availability == ToolchainAvailability.Found;

    /// <summary>
    /// Gets the curated remediation action label for this tool, or <see langword="null"/> when it is
    /// found or has no curated action. Papyrus gets "Find manually" (a real manual-path override
    /// exists for it); CMake is bundled with Visual Studio, so its remediation points at the installer
    /// rather than implying a manual path override the Settings page does not expose.
    /// </summary>
    public string? RemediationActionLabel => IsAvailable
        ? null
        : ToolName switch
        {
            "Papyrus Compiler" => "Find manually",
            "CMake" => "Open Visual Studio Installer",
            _ => null,
        };
}
