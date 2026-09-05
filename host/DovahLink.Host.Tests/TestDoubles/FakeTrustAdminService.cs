using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A controllable <see cref="ITrustAdminService"/> stand-in. <see cref="RenameAsync"/> and
/// <see cref="TryCaptureTrustedIncarnation"/> serve the client message dispatcher's own tests;
/// <see cref="List"/>, <see cref="Help"/>, and every <c>ByShortIdAsync</c> mutation serve the
/// adapter trust-admin request handler's tests. <see cref="RevokeAsync"/>, <see cref="BlockAsync"/>,
/// <see cref="UnblockAsync"/>, and <see cref="ForgetAsync"/> are not called by either current
/// consumer and remain unimplemented.
/// </summary>
public sealed class FakeTrustAdminService : ITrustAdminService
{
    /// <summary>The most recent call to <see cref="RenameAsync"/>, or <see langword="null"/> if it was never called.</summary>
    public (ClientId ClientId, string DisplayName, KnownDeviceIncarnationId ExpectedIncarnation)? LastRenameCall { get; private set; }

    /// <summary>When set, <see cref="RenameAsync"/> throws this instead of recording the call.</summary>
    public Exception? ThrowOnRename { get; set; }

    /// <summary>
    /// The value <see cref="TryCaptureTrustedIncarnation"/> returns, or <see langword="null"/> to
    /// simulate an unrecognized or not-currently-Trusted identity. Defaults to a fresh incarnation so a
    /// test exercising <see cref="RenameAsync"/>'s own outcome mapping does not need to configure this
    /// unless the capture step itself is what it is testing.
    /// </summary>
    public KnownDeviceIncarnationId? IncarnationToCapture { get; set; } = KnownDeviceIncarnationId.NewId();

    /// <summary>The records <see cref="List"/> returns for every scope.</summary>
    public IReadOnlyList<TrustRecord> ListResult { get; set; } = [];

    /// <summary>The scope strings passed to <see cref="List"/>, in call order.</summary>
    public List<string> ListScopeCalls { get; } = [];

    /// <summary>The text <see cref="Help"/> returns.</summary>
    public string HelpResult { get; set; } = string.Empty;

    /// <summary>The outcome <see cref="RevokeByShortIdAsync"/> returns.</summary>
    public TrustMutationOutcome RevokeByShortIdResult { get; set; } = TrustMutationOutcome.Changed;

    /// <summary>The outcome <see cref="BlockByShortIdAsync"/> returns.</summary>
    public TrustMutationOutcome BlockByShortIdResult { get; set; } = TrustMutationOutcome.Changed;

    /// <summary>The outcome <see cref="UnblockByShortIdAsync"/> returns.</summary>
    public TrustMutationOutcome UnblockByShortIdResult { get; set; } = TrustMutationOutcome.Changed;

    /// <summary>The outcome <see cref="ForgetByShortIdAsync"/> returns.</summary>
    public TrustMutationOutcome ForgetByShortIdResult { get; set; } = TrustMutationOutcome.Changed;

    /// <summary>The short IDs passed to <see cref="RevokeByShortIdAsync"/>, in call order.</summary>
    public List<string> RevokeByShortIdCalls { get; } = [];

    /// <summary>The short IDs passed to <see cref="BlockByShortIdAsync"/>, in call order.</summary>
    public List<string> BlockByShortIdCalls { get; } = [];

    /// <summary>The short IDs passed to <see cref="UnblockByShortIdAsync"/>, in call order.</summary>
    public List<string> UnblockByShortIdCalls { get; } = [];

    /// <summary>The short IDs passed to <see cref="ForgetByShortIdAsync"/>, in call order.</summary>
    public List<string> ForgetByShortIdCalls { get; } = [];

    /// <summary>The clients <see cref="ResetTrustAsync"/> returns as affected.</summary>
    public IReadOnlyList<ClientId> ResetTrustResult { get; set; } = [];

    /// <inheritdoc/>
    public Task RenameAsync(ClientId clientId, string displayName, KnownDeviceIncarnationId expectedIncarnation, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRename is { } exception)
        {
            throw exception;
        }

        LastRenameCall = (clientId, displayName, expectedIncarnation);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public KnownDeviceIncarnationId? TryCaptureTrustedIncarnation(ClientId clientId) => IncarnationToCapture;

    /// <inheritdoc/>
    public IReadOnlyList<TrustRecord> List(string scope = "all")
    {
        ListScopeCalls.Add(scope);
        return ListResult;
    }

    /// <inheritdoc/>
    public string Help() => HelpResult;

    /// <summary>Not called by either current consumer.</summary>
    public Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by either current consumer.</summary>
    public Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by either current consumer.</summary>
    public Task UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by either current consumer.</summary>
    public Task ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default) => Task.FromResult(ResetTrustResult);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RevokeByShortIdAsync(string shortId, CancellationToken cancellationToken = default)
    {
        RevokeByShortIdCalls.Add(shortId);
        return Task.FromResult(RevokeByShortIdResult);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> BlockByShortIdAsync(string shortId, CancellationToken cancellationToken = default)
    {
        BlockByShortIdCalls.Add(shortId);
        return Task.FromResult(BlockByShortIdResult);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> UnblockByShortIdAsync(string shortId, CancellationToken cancellationToken = default)
    {
        UnblockByShortIdCalls.Add(shortId);
        return Task.FromResult(UnblockByShortIdResult);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default)
    {
        ForgetByShortIdCalls.Add(shortId);
        return Task.FromResult(ForgetByShortIdResult);
    }
}
