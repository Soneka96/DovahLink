using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies git branch, working tree, and remote sync reporting.</summary>
public sealed class GitStatusServiceTests
{
    /// <summary>The fetch timeout used by every test, short enough that the timeout test does not slow the suite.</summary>
    private static readonly TimeSpan TestFetchTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Reports a clean tree with commits already pushed to the upstream remote.</summary>
    [Fact]
    public async Task GetStatusReportsCleanAndPushed()
    {
        var runner = new FakeCommandRunner();
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal("main", status.Branch);
        Assert.Equal(WorkingTreeState.Clean, status.WorkingTreeState);
        Assert.Equal(RemoteSyncState.Pushed, status.RemoteSyncState);
        Assert.Equal("abc123", status.CommitSha);
    }

    /// <summary>Reports a dirty tree from nonempty porcelain status output.</summary>
    [Fact]
    public async Task GetStatusReportsDirtyFromNonemptyPorcelainOutput()
    {
        var runner = new FakeCommandRunner { StatusOutputLines = [" M Ui/BuildPage.xaml"] };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(WorkingTreeState.Dirty, status.WorkingTreeState);
    }

    /// <summary>Reports not-pushed when the branch has commits ahead of its upstream remote.</summary>
    [Fact]
    public async Task GetStatusReportsNotPushedWhenAheadOfUpstream()
    {
        var runner = new FakeCommandRunner { AheadCountOutputLines = ["3"] };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(RemoteSyncState.NotPushed, status.RemoteSyncState);
    }

    /// <summary>Reports could-not-verify when fetch fails on an otherwise clean, committed tree.</summary>
    [Fact]
    public async Task GetStatusReportsCouldNotVerifyWhenFetchFailsOnACleanTree()
    {
        var runner = new FakeCommandRunner { FetchExitCode = 1 };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(WorkingTreeState.Clean, status.WorkingTreeState);
        Assert.Equal(RemoteSyncState.CouldNotVerify, status.RemoteSyncState);
    }

    /// <summary>Reports could-not-verify when fetch succeeds but the branch has no configured upstream.</summary>
    [Fact]
    public async Task GetStatusReportsCouldNotVerifyWithNoConfiguredUpstream()
    {
        var runner = new FakeCommandRunner { AheadCountExitCode = 128, AheadCountOutputLines = [] };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(RemoteSyncState.CouldNotVerify, status.RemoteSyncState);
    }

    /// <summary>Reports could-not-verify when the fetch executable cannot be started.</summary>
    [Fact]
    public async Task GetStatusReportsCouldNotVerifyWhenFetchCannotStart()
    {
        var runner = new FakeCommandRunner { FetchThrowsWin32Exception = true };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(RemoteSyncState.CouldNotVerify, status.RemoteSyncState);
    }

    /// <summary>Reports could-not-verify when fetch does not finish before the timeout.</summary>
    [Fact]
    public async Task GetStatusReportsCouldNotVerifyWhenFetchTimesOut()
    {
        var runner = new FakeCommandRunner { FetchHangsUntilCancelled = true };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(RemoteSyncState.CouldNotVerify, status.RemoteSyncState);
    }

    /// <summary>Propagates cancellation from the caller's own token rather than reporting it as a timeout.</summary>
    [Fact]
    public async Task GetStatusPropagatesCallerCancellationRatherThanReportingCouldNotVerify()
    {
        var runner = new FakeCommandRunner { FetchHangsUntilCancelled = true };
        var service = new GitStatusService(runner, TimeSpan.FromSeconds(30));
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetStatusAsync(@"C:\repo", callerCancellation.Token));
    }

    /// <summary>Throws when the branch lookup fails, rather than reporting a guessed status.</summary>
    [Fact]
    public async Task GetStatusThrowsWhenTheBranchLookupFails()
    {
        var runner = new FakeCommandRunner { BranchExitCode = 128 };
        var service = new GitStatusService(runner, TestFetchTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(@"C:\repo"));
    }

    /// <summary>
    /// Reports the documented InvalidOperationException, not a raw process-start exception, when
    /// <c>git</c> itself cannot be started -- for example because it is missing from PATH.
    /// </summary>
    [Fact]
    public async Task GetStatusThrowsInvalidOperationExceptionWhenGitCannotBeStarted()
    {
        var runner = new FakeCommandRunner { BranchThrowsWin32Exception = true };
        var service = new GitStatusService(runner, TestFetchTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(@"C:\repo"));
    }

    /// <summary>Throws when the working tree status lookup fails, rather than reporting a guessed status.</summary>
    [Fact]
    public async Task GetStatusThrowsWhenTheWorkingTreeStatusLookupFails()
    {
        var runner = new FakeCommandRunner { StatusExitCode = 128 };
        var service = new GitStatusService(runner, TestFetchTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(@"C:\repo"));
    }

    /// <summary>Throws when the commit SHA lookup fails, rather than reporting a guessed status.</summary>
    [Fact]
    public async Task GetStatusThrowsWhenTheCommitShaLookupFails()
    {
        var runner = new FakeCommandRunner { RevParseHeadExitCode = 128 };
        var service = new GitStatusService(runner, TestFetchTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStatusAsync(@"C:\repo"));
    }

    /// <summary>Reports could-not-verify when the ahead count cannot be parsed as an integer.</summary>
    [Fact]
    public async Task GetStatusReportsCouldNotVerifyWhenTheAheadCountIsUnparsable()
    {
        var runner = new FakeCommandRunner { AheadCountOutputLines = ["fatal: no upstream configured"] };
        var service = new GitStatusService(runner, TestFetchTimeout);

        GitSourceStatus status = await service.GetStatusAsync(@"C:\repo");

        Assert.Equal(RemoteSyncState.CouldNotVerify, status.RemoteSyncState);
    }

    /// <summary>Sends every command through the runner with the repository root as the working directory and no shell interpolation.</summary>
    [Fact]
    public async Task GetStatusRunsEveryCommandInTheRepositoryRootWithStructuredArguments()
    {
        var runner = new FakeCommandRunner();
        var service = new GitStatusService(runner, TestFetchTimeout);

        await service.GetStatusAsync(@"C:\repo");

        Assert.All(runner.Commands, command =>
        {
            Assert.Equal("git", command.ExecutablePath);
            Assert.Equal(@"C:\repo", command.WorkingDirectory);
        });
        Assert.Contains(runner.Commands, command => command.Arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]));
        Assert.Contains(runner.Commands, command => command.Arguments.SequenceEqual(["status", "--porcelain"]));
        Assert.Contains(runner.Commands, command => command.Arguments.SequenceEqual(["rev-parse", "HEAD"]));
        Assert.Contains(runner.Commands, command => command.Arguments.SequenceEqual(["fetch"]));
        Assert.Contains(runner.Commands, command => command.Arguments.SequenceEqual(["rev-list", "--count", "@{u}..HEAD"]));
    }

    /// <summary>Routes each git subcommand to its configured output and exit code.</summary>
    private sealed class FakeCommandRunner : ICommandRunner
    {
        /// <summary>Gets the standard-output lines for <c>git rev-parse --abbrev-ref HEAD</c>.</summary>
        public IReadOnlyList<string> BranchOutputLines { get; init; } = ["main"];

        /// <summary>Gets the exit code for <c>git rev-parse --abbrev-ref HEAD</c>.</summary>
        public int BranchExitCode { get; init; }

        /// <summary>Gets whether <c>git rev-parse --abbrev-ref HEAD</c> throws <see cref="Win32Exception"/> to simulate a missing git executable.</summary>
        public bool BranchThrowsWin32Exception { get; init; }

        /// <summary>Gets the standard-output lines for <c>git status --porcelain</c>.</summary>
        public IReadOnlyList<string> StatusOutputLines { get; init; } = [];

        /// <summary>Gets the exit code for <c>git status --porcelain</c>.</summary>
        public int StatusExitCode { get; init; }

        /// <summary>Gets the standard-output lines for <c>git rev-parse HEAD</c>.</summary>
        public IReadOnlyList<string> RevParseHeadOutputLines { get; init; } = ["abc123"];

        /// <summary>Gets the exit code for <c>git rev-parse HEAD</c>.</summary>
        public int RevParseHeadExitCode { get; init; }

        /// <summary>Gets the exit code for <c>git fetch</c>.</summary>
        public int FetchExitCode { get; init; }

        /// <summary>Gets whether <c>git fetch</c> throws <see cref="Win32Exception"/> to simulate a missing executable.</summary>
        public bool FetchThrowsWin32Exception { get; init; }

        /// <summary>Gets whether <c>git fetch</c> waits until cancelled to simulate an unresponsive process.</summary>
        public bool FetchHangsUntilCancelled { get; init; }

        /// <summary>Gets the standard-output lines for <c>git rev-list --count @{u}..HEAD</c>.</summary>
        public IReadOnlyList<string> AheadCountOutputLines { get; init; } = ["0"];

        /// <summary>Gets the exit code for <c>git rev-list --count @{u}..HEAD</c>.</summary>
        public int AheadCountExitCode { get; init; }

        /// <summary>Gets the ordered commands supplied to the runner.</summary>
        public List<BuildCommand> Commands { get; } = [];

        /// <summary>Records the command invocation and routes it to its configured outcome by subcommand.</summary>
        public async Task<int> RunAsync(
            BuildCommand command,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            switch (command.Arguments[0])
            {
                case "rev-parse" when command.Arguments.Contains("--abbrev-ref"):
                    if (BranchThrowsWin32Exception)
                    {
                        throw new Win32Exception("The system cannot find the file specified.");
                    }
                    Emit(BranchOutputLines, onStandardOutput);
                    return BranchExitCode;
                case "status":
                    Emit(StatusOutputLines, onStandardOutput);
                    return StatusExitCode;
                case "rev-parse":
                    Emit(RevParseHeadOutputLines, onStandardOutput);
                    return RevParseHeadExitCode;
                case "fetch":
                    if (FetchThrowsWin32Exception)
                    {
                        throw new Win32Exception("The system cannot find the file specified.");
                    }
                    if (FetchHangsUntilCancelled)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    return FetchExitCode;
                case "rev-list":
                    Emit(AheadCountOutputLines, onStandardOutput);
                    return AheadCountExitCode;
                default:
                    throw new InvalidOperationException($"Unexpected git subcommand: {command.Arguments[0]}");
            }
        }

        /// <summary>Invokes the standard-output callback once per configured line.</summary>
        private static void Emit(IReadOnlyList<string> lines, Action<string>? onStandardOutput)
        {
            foreach (string line in lines)
            {
                onStandardOutput?.Invoke(line);
            }
        }
    }
}
