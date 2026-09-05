namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host to ask the adapter to present a pairing code at its Skyrim-facing display seam:
/// initial display, manual redisplay (<c>pairing_renotify</c>), or best-effort wrong-code automatic
/// redisplay. The host never discloses <see cref="Code"/> through any other channel; see
/// <c>ai/context/protocol/security.md</c>'s "Pairing codes travel only over the mutually
/// authenticated private channel".
/// </summary>
/// <param name="CorrelationId">The nonzero request identity the adapter's <see cref="IpcPairingDisplayAckMessage"/> reply correlates to.</param>
/// <param name="Code">The exact code to display, always <see cref="Constants.PairingChallengeCodeDigits"/> ASCII decimal digits.</param>
/// <param name="Mode">Which display intent this request carries.</param>
public sealed record IpcPairingDisplayMessage(ulong CorrelationId, string Code, PairingDisplayMode Mode) : IpcMessage(CorrelationId);
