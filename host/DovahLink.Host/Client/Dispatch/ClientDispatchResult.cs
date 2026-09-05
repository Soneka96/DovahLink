namespace DovahLink.Host.Client.Dispatch;

/// <summary>
/// The side effects of one <see cref="IClientMessageDispatcher.DispatchAsync"/> call that only the
/// caller -- the connection handler owning per-connection session state -- can apply.
/// </summary>
/// <param name="UpgradeToFullTrust">
/// Whether a successful <c>pairing_ack</c> requires the caller to upgrade this connection's own
/// session to full trust, in place, exactly once.
/// </param>
/// <param name="IsProtocolViolation">
/// Whether this dispatch already sent its own canonical error response for a malformed payload and
/// the caller must still apply its own protocol-violation count and close policy, without sending a
/// second error.
/// </param>
public sealed record ClientDispatchResult(bool UpgradeToFullTrust = false, bool IsProtocolViolation = false);
