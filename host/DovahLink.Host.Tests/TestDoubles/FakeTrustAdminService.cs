using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A narrow <see cref="ITrustAdminService"/> stand-in exercising only <see cref="RenameAsync"/>, the
/// sole member the client message dispatcher under test calls. Every other member is not called by
/// that consumer and is not implemented.
/// </summary>
public sealed class FakeTrustAdminService : ITrustAdminService
{
    /// <summary>The most recent call to <see cref="RenameAsync"/>, or <see langword="null"/> if it was never called.</summary>
    public (ClientId ClientId, string DisplayName)? LastRenameCall { get; private set; }

    /// <summary>When set, <see cref="RenameAsync"/> throws this instead of recording the call.</summary>
    public Exception? ThrowOnRename { get; set; }

    /// <inheritdoc/>
    public Task RenameAsync(ClientId clientId, string displayName, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRename is { } exception)
        {
            throw exception;
        }

        LastRenameCall = (clientId, displayName);
        return Task.CompletedTask;
    }

    /// <summary>Not called by the dispatcher under test.</summary>
    public IReadOnlyList<TrustRecord> List(string scope = "all") => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public string Help() => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task<TrustMutationOutcome> RevokeByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task<TrustMutationOutcome> BlockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task<TrustMutationOutcome> UnblockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Not called by the dispatcher under test.</summary>
    public Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
