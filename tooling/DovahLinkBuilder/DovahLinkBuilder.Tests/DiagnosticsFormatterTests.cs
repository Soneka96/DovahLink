using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the plain-text diagnostics report's content and stable formatting.</summary>
public sealed class DiagnosticsFormatterTests
{
    /// <summary>Includes every check's tool name and availability.</summary>
    [Fact]
    public void FormatIncludesEveryChecksNameAndAvailability()
    {
        string report = DiagnosticsFormatter.Format(
            [
                new ToolchainCheckResult("CMake", ToolchainAvailability.Found, "3.30.0", null),
                new ToolchainCheckResult("Papyrus Compiler", ToolchainAvailability.Missing, null, "not found"),
            ],
            gitStatus: null,
            gitStatusError: null,
            lastOutcome: null,
            lastOutcomeMessage: null);

        Assert.Contains("CMake: Found", report);
        Assert.Contains("Papyrus Compiler: Missing", report);
    }

    /// <summary>Includes a check's detail and remediation hint when present.</summary>
    [Fact]
    public void FormatIncludesDetailAndRemediationHintWhenPresent()
    {
        string report = DiagnosticsFormatter.Format(
            [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")],
            gitStatus: null,
            gitStatusError: null,
            lastOutcome: null,
            lastOutcomeMessage: null);

        Assert.Contains("not on PATH", report);
    }

    /// <summary>Includes the branch, working tree, remote sync, and commit SHA when git status loaded successfully.</summary>
    [Fact]
    public void FormatIncludesGitStatusFieldsWhenLoaded()
    {
        string report = DiagnosticsFormatter.Format(
            [],
            gitStatus: new GitSourceStatus("feature/x", WorkingTreeState.Dirty, RemoteSyncState.NotPushed, "abcdef1234567890"),
            gitStatusError: null,
            lastOutcome: null,
            lastOutcomeMessage: null);

        Assert.Contains("feature/x", report);
        Assert.Contains("Dirty", report);
        Assert.Contains("NotPushed", report);
        Assert.Contains("abcdef1234567890", report);
    }

    /// <summary>Includes the failure message when git status could not be determined.</summary>
    [Fact]
    public void FormatIncludesTheGitStatusErrorWhenItCouldNotBeDetermined()
    {
        string report = DiagnosticsFormatter.Format(
            [], gitStatus: null, gitStatusError: "not a git repository", lastOutcome: null, lastOutcomeMessage: null);

        Assert.Contains("not a git repository", report);
    }

    /// <summary>Reports git status as not yet loaded when there is neither a status nor an error.</summary>
    [Fact]
    public void FormatReportsGitStatusNotYetLoadedWhenNeitherStatusNorErrorIsPresent()
    {
        string report = DiagnosticsFormatter.Format([], gitStatus: null, gitStatusError: null, lastOutcome: null, lastOutcomeMessage: null);

        Assert.Contains("Not yet loaded", report);
    }

    /// <summary>Includes the outcome and failure message for the most recently finished build.</summary>
    [Fact]
    public void FormatIncludesTheLastBuildsOutcomeAndFailureMessage()
    {
        string report = DiagnosticsFormatter.Format(
            [], gitStatus: null, gitStatusError: null, lastOutcome: BuildHistoryResult.Failed, lastOutcomeMessage: "the adapter build failed");

        Assert.Contains("Failed", report);
        Assert.Contains("the adapter build failed", report);
    }

    /// <summary>Omits the message line for a successful build, which never carries a failure message.</summary>
    [Fact]
    public void FormatOmitsTheMessageLineForASuccessfulBuild()
    {
        string report = DiagnosticsFormatter.Format(
            [], gitStatus: null, gitStatusError: null, lastOutcome: BuildHistoryResult.Succeeded, lastOutcomeMessage: null);

        Assert.Contains("Outcome: Succeeded", report);
        Assert.DoesNotContain("Message:", report);
    }

    /// <summary>Reports that no build has run yet when there is no last outcome.</summary>
    [Fact]
    public void FormatReportsNoBuildHasRunYetWhenThereIsNoLastOutcome()
    {
        string report = DiagnosticsFormatter.Format([], gitStatus: null, gitStatusError: null, lastOutcome: null, lastOutcomeMessage: null);

        Assert.Contains("No build has run yet", report);
    }

    /// <summary>Produces stable, deterministic formatting for a representative full-state input.</summary>
    [Fact]
    public void FormatProducesStableOutputForARepresentativeFullState()
    {
        string report = DiagnosticsFormatter.Format(
            [
                new ToolchainCheckResult("Repository", ToolchainAvailability.Found, @"C:\repo", null),
                new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH"),
            ],
            gitStatus: new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
            gitStatusError: null,
            lastOutcome: BuildHistoryResult.Failed,
            lastOutcomeMessage: "the adapter build failed");

        string expected = string.Join(
            Environment.NewLine,
            "DovahLink Builder diagnostics",
            string.Empty,
            "Environment:",
            @"  Repository: Found (C:\repo)",
            "  CMake: Missing -- not on PATH",
            string.Empty,
            "Git status:",
            "  Branch: main",
            "  Working tree: Clean",
            "  Remote sync: Pushed",
            "  Commit: abc123",
            string.Empty,
            "Last build:",
            "  Outcome: Failed",
            "  Message: the adapter build failed");
        Assert.Equal(expected, report);
    }
}
