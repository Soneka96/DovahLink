using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Formats a plain-text diagnostics report -- environment checks, git status, and the last build -- for pasting into a bug report.</summary>
public static class DiagnosticsFormatter
{
    /// <summary>Formats a plain-text diagnostics report from the Build page's current state.</summary>
    /// <param name="preflightResults">The most recently loaded preflight results, in preflight order.</param>
    /// <param name="gitStatus">The most recently loaded git status, or <see langword="null"/> when it could not be determined.</param>
    /// <param name="gitStatusError">The git status failure message, when <paramref name="gitStatus"/> is <see langword="null"/>; otherwise ignored.</param>
    /// <param name="lastOutcome">The most recently finished build's outcome, or <see langword="null"/> before any build has finished.</param>
    /// <param name="lastOutcomeMessage">The most recently finished build's failure message, when it failed; otherwise ignored.</param>
    /// <returns>The formatted diagnostics report.</returns>
    public static string Format(
        IReadOnlyList<ToolchainCheckResult> preflightResults,
        GitSourceStatus? gitStatus,
        string? gitStatusError,
        BuildHistoryResult? lastOutcome,
        string? lastOutcomeMessage)
    {
        List<string> lines = ["DovahLink Builder diagnostics", string.Empty, "Environment:"];
        lines.AddRange(preflightResults.Select(FormatCheck));

        lines.Add(string.Empty);
        lines.Add("Git status:");
        lines.AddRange(FormatGitStatus(gitStatus, gitStatusError));

        lines.Add(string.Empty);
        lines.Add("Last build:");
        lines.AddRange(FormatLastBuild(lastOutcome, lastOutcomeMessage));

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Formats one preflight check's name, availability, detail, and remediation hint on a single line.</summary>
    /// <param name="result">The check to format.</param>
    private static string FormatCheck(ToolchainCheckResult result)
    {
        string line = $"  {result.ToolName}: {result.Availability}";
        if (result.Detail is not null)
        {
            line += $" ({result.Detail})";
        }

        if (result.RemediationHint is not null)
        {
            line += $" -- {result.RemediationHint}";
        }

        return line;
    }

    /// <summary>Formats the git status section's lines.</summary>
    /// <param name="gitStatus">The loaded git status, or <see langword="null"/> when it could not be determined.</param>
    /// <param name="gitStatusError">The failure message, when <paramref name="gitStatus"/> is <see langword="null"/>; otherwise ignored.</param>
    private static IEnumerable<string> FormatGitStatus(GitSourceStatus? gitStatus, string? gitStatusError)
    {
        if (gitStatus is null)
        {
            yield return gitStatusError is not null ? $"  Could not be determined: {gitStatusError}" : "  Not yet loaded.";
            yield break;
        }

        yield return $"  Branch: {gitStatus.Branch}";
        yield return $"  Working tree: {gitStatus.WorkingTreeState}";
        yield return $"  Remote sync: {gitStatus.RemoteSyncState}";
        yield return $"  Commit: {gitStatus.CommitSha}";
    }

    /// <summary>Formats the last build section's lines.</summary>
    /// <param name="lastOutcome">The most recently finished build's outcome, or <see langword="null"/> before any build has finished.</param>
    /// <param name="lastOutcomeMessage">The failure message, when <paramref name="lastOutcome"/> is Failed; otherwise ignored.</param>
    private static IEnumerable<string> FormatLastBuild(BuildHistoryResult? lastOutcome, string? lastOutcomeMessage)
    {
        if (lastOutcome is null)
        {
            yield return "  No build has run yet.";
            yield break;
        }

        yield return $"  Outcome: {lastOutcome}";
        if (lastOutcomeMessage is not null)
        {
            yield return $"  Message: {lastOutcomeMessage}";
        }
    }
}
