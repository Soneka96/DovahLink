using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// A narrow <see cref="ITrustAdminService"/> stand-in exercising only <see cref="RenameAsync"/> and
/// <see cref="TryCaptureTrustedIncarnation"/>, the sole members the client message dispatcher under test
/// calls. Every other member is not called by that consumer and is not implemented.
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
