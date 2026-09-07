namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter to forward one Papyrus-originated trust-administration command to the host,
/// the sole mutation authority. Every operation carries only the argument its closed shape requires;
/// see <c>ai/context/protocol/security.md</c>'s "Trust administration surface" for the exact
/// operation set this mirrors.
/// </summary>
/// <param name="CorrelationId">The nonzero request identity the host's <see cref="IpcTrustAdminResultMessage"/> reply correlates to.</param>
/// <param name="Operation">Which trust-administration command this request carries.</param>
/// <param name="ListScope">The device scope for <see cref="TrustAdminOperation.List"/>; otherwise <see langword="null"/>.</param>
/// <param name="ShortId">
/// The five-digit device identity for <see cref="TrustAdminOperation.Revoke"/>,
/// <see cref="TrustAdminOperation.Block"/>, <see cref="TrustAdminOperation.Unblock"/>, or
/// <see cref="TrustAdminOperation.Forget"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="ConfirmationCode">
/// The six-digit Factory Reset confirmation code for <see cref="TrustAdminOperation.ConfirmReset"/>;
/// otherwise <see langword="null"/>.
/// </param>
public sealed record IpcTrustAdminRequestMessage(
    ulong CorrelationId,
    TrustAdminOperation Operation,
    TrustAdminListScope? ListScope = null,
    string? ShortId = null,
    string? ConfirmationCode = null) : IpcMessage(CorrelationId);
