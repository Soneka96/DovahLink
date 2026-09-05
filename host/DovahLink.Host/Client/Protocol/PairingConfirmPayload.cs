namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>pairing_confirm</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>pairing_confirm</c>" section. Restricted-session only: the client's submission of the
/// six-digit code read from Skyrim.
/// </summary>
public sealed record PairingConfirmPayload
{
    /// <summary>The six-digit code to evaluate against the requesting client's owned active challenge.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// The optional presentation-only label for the resulting trusted client. Absent or explicit
    /// <see langword="null"/> preserves an existing display name on re-pair; a present value,
    /// including an empty string, always replaces it.
    /// </summary>
    public string? DisplayName { get; init; }
}
