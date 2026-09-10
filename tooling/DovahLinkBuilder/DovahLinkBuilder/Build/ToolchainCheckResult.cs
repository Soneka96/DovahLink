namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Reports one non-throwing preflight check for a required build tool.</summary>
/// <param name="ToolName">The human-readable name of the tool being checked.</param>
/// <param name="Availability">Whether the tool is available.</param>
/// <param name="Detail">
/// The resolved location of the tool when <paramref name="Availability"/> is
/// <see cref="ToolchainAvailability.Found"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="RemediationHint">
/// A human-readable explanation of what is missing or invalid, and where the tool is expected,
/// when <paramref name="Availability"/> is not <see cref="ToolchainAvailability.Found"/>; otherwise
/// <see langword="null"/>.
/// </param>
public sealed record ToolchainCheckResult(
    string ToolName,
    ToolchainAvailability Availability,
    string? Detail,
    string? RemediationHint);
