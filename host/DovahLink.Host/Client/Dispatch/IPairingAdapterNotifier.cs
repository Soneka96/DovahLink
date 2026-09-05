namespace DovahLink.Host.Client.Dispatch;

/// <summary>
/// The narrow, host-owned seam through which the client message dispatcher requests a Skyrim-facing
/// pairing notification. The adapter performs only presentation/glue; every pairing, trust,
/// authorization, and retry decision remains host-owned, per
/// <c>ai/context/protocol/security.md</c>'s "No client request directly invokes Skyrim code" boundary.
/// A later concept implements this over the private adapter IPC channel; this concept only depends on
/// and calls it.
/// </summary>
public interface IPairingAdapterNotifier
{
    /// <summary>
    /// Requests the adapter display a freshly generated pairing code for the first time. Bounded and
    /// correlation-scoped to the current adapter connection generation; the caller must treat a
    /// negative or timed-out acknowledgement identically -- the code must never be reported available.
    /// </summary>
    /// <param name="code">The six-digit code to display.</param>
    /// <param name="cancellationToken">The token used to bound the wait for the adapter's acknowledgement.</param>
    /// <returns><see langword="true"/> once the adapter accepted the display; <see langword="false"/> on rejection or timeout.</returns>
    Task<bool> TryNotifyCodeAvailableAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the adapter redisplay the currently active code after an explicit client
    /// <c>pairing_renotify</c> request. Bounded the same way as <see cref="TryNotifyCodeAvailableAsync"/>.
    /// </summary>
    /// <param name="code">The still-active code to redisplay.</param>
    /// <param name="cancellationToken">The token used to bound the wait for the adapter's acknowledgement.</param>
    /// <returns><see langword="true"/> once the adapter accepted the redisplay; <see langword="false"/> on rejection or timeout.</returns>
    Task<bool> TryNotifyRedisplayAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the adapter redisplay the currently active code with incorrect-attempt presentation,
    /// after a wrong evaluated code and the pairing coordinator's own independent auto-renotify
    /// cooldown permit it. Best effort: never blocks or changes the already-decided code-confirmation
    /// result, and the code is never disclosed through the public wire response.
    /// </summary>
    /// <param name="code">The still-active code to redisplay.</param>
    /// <param name="cancellationToken">The token used to bound the underlying notification.</param>
    Task NotifyCodeIncorrectAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the adapter present a no-code terminal notification once the wrong-attempt hard limit
    /// cancels the active challenge. Best effort, and carries no code.
    /// </summary>
    /// <param name="cancellationToken">The token used to bound the underlying notification.</param>
    Task NotifyAttemptsExhaustedAsync(CancellationToken cancellationToken);
}
