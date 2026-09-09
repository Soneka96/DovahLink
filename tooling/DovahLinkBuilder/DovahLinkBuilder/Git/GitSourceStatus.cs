namespace DovahLink.DovahLinkBuilder.Git;

/// <summary>Reports the repository's current branch, working tree, and remote sync state.</summary>
/// <param name="Branch">The current branch name.</param>
/// <param name="WorkingTreeState">Whether the working tree has uncommitted changes.</param>
/// <param name="RemoteSyncState">Whether the current branch's commits are pushed to its upstream remote.</param>
/// <param name="CommitSha">The full SHA of the current commit.</param>
public sealed record GitSourceStatus(
    string Branch,
    WorkingTreeState WorkingTreeState,
    RemoteSyncState RemoteSyncState,
    string CommitSha);
