using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Git;

/// <summary>Reports the repository's git branch, working tree, and remote sync state.</summary>
public interface IGitStatusService
{
    /// <summary>Checks the repository's current branch, working tree, and remote sync state.</summary>
    /// <param name="repositoryRoot">The repository root to check.</param>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    /// <returns>The repository's current git status.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the branch, working tree, or commit lookup fails, for example because
    /// <paramref name="repositoryRoot"/> is not a git repository.
    /// </exception>
    Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default);
}

/// <summary>Reports the repository's git branch, working tree, and remote sync state.</summary>
public sealed class GitStatusService : IGitStatusService
{
    /// <summary>The runner used to shell git commands.</summary>
    private readonly ICommandRunner commandRunner;

    /// <summary>The maximum time <c>git fetch</c> waits before remote sync is reported as unverifiable.</summary>
    private readonly TimeSpan fetchTimeout;

    /// <summary>Creates a git status service over the given command runner.</summary>
    /// <param name="commandRunner">The runner used to shell git commands.</param>
    public GitStatusService(ICommandRunner commandRunner)
        : this(commandRunner, Constants.GitFetchTimeout)
    {
    }

    /// <summary>Creates a git status service with a controllable fetch-timeout seam.</summary>
    /// <param name="commandRunner">The runner used to shell git commands.</param>
    /// <param name="fetchTimeout">The maximum time <c>git fetch</c> waits before remote sync is reported as unverifiable.</param>
    internal GitStatusService(ICommandRunner commandRunner, TimeSpan fetchTimeout)
    {
        this.commandRunner = commandRunner;
        this.fetchTimeout = fetchTimeout;
    }

    /// <inheritdoc/>
    public async Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        string branch = await RunGitAsync(repositoryRoot, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        string statusOutput = await RunGitAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);
        string commitSha = await RunGitAsync(repositoryRoot, ["rev-parse", "HEAD"], cancellationToken);
        RemoteSyncState remoteSyncState = await CheckRemoteSyncAsync(repositoryRoot, cancellationToken);

        WorkingTreeState workingTreeState = statusOutput.Length == 0 ? WorkingTreeState.Clean : WorkingTreeState.Dirty;
        return new GitSourceStatus(branch, workingTreeState, remoteSyncState, commitSha);
    }

    /// <summary>
    /// Fetches the upstream remote and compares the current branch against it, reporting the result
    /// instead of throwing: a failed, timed-out, or unstartable fetch, or a branch with no configured
    /// upstream, all report <see cref="RemoteSyncState.CouldNotVerify"/> rather than a stale guess.
    /// </summary>
    private async Task<RemoteSyncState> CheckRemoteSyncAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(fetchTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            int fetchExitCode = await RunGitCommandAsync(repositoryRoot, ["fetch"], null, linkedSource.Token);
            if (fetchExitCode != 0)
            {
                return RemoteSyncState.CouldNotVerify;
            }
        }
        catch (Win32Exception)
        {
            return RemoteSyncState.CouldNotVerify;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return RemoteSyncState.CouldNotVerify;
        }

        List<string> aheadCountLines = [];
        int aheadExitCode = await RunGitCommandAsync(
            repositoryRoot, ["rev-list", "--count", "@{u}..HEAD"], aheadCountLines.Add, cancellationToken);
        if (aheadExitCode != 0 || !int.TryParse(string.Concat(aheadCountLines).Trim(), out int aheadCount))
        {
            return RemoteSyncState.CouldNotVerify;
        }

        return aheadCount == 0 ? RemoteSyncState.Pushed : RemoteSyncState.NotPushed;
    }

    /// <summary>Runs a git command expected to succeed, returning its trimmed standard output.</summary>
    /// <param name="repositoryRoot">The repository root in which to run the command.</param>
    /// <param name="arguments">The git subcommand and its arguments.</param>
    /// <param name="cancellationToken">The token used to cancel the command.</param>
    /// <exception cref="InvalidOperationException">Thrown when the command exits with a nonzero code.</exception>
    private async Task<string> RunGitAsync(string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        List<string> outputLines = [];
        int exitCode = await RunGitCommandAsync(repositoryRoot, arguments, outputLines.Add, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited with code {exitCode} in {repositoryRoot}.");
        }
        return string.Join(Environment.NewLine, outputLines).Trim();
    }

    /// <summary>Runs a structured git command through <see cref="commandRunner"/>.</summary>
    /// <param name="repositoryRoot">The repository root in which to run the command.</param>
    /// <param name="arguments">The git subcommand and its arguments.</param>
    /// <param name="onStandardOutput">The callback invoked for each standard-output line, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token used to cancel the command.</param>
    /// <returns>The process exit code.</returns>
    private Task<int> RunGitCommandAsync(
        string repositoryRoot, IReadOnlyList<string> arguments, Action<string>? onStandardOutput, CancellationToken cancellationToken)
    {
        var command = new BuildCommand("git", arguments, repositoryRoot, new Dictionary<string, string>());
        return commandRunner.RunAsync(command, onStandardOutput, null, cancellationToken);
    }
}
