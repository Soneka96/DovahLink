namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>error</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>error</c>" section.
/// Host-originated only. <see cref="Message"/> is diagnostic text and must never be used for
/// branching; <see cref="Code"/> is the sole canonical, machine-readable value for that purpose.
/// </summary>
public sealed record ErrorPayload
{
    /// <summary>The canonical machine-readable error code.</summary>
    public required PublicProtocolErrorCode Code { get; init; }

    /// <summary>Diagnostic text, redacted of credentials, filesystem paths, and raw infrastructure exceptions.</summary>
    public required string Message { get; init; }

    /// <summary>Whether the same request may reasonably be retried.</summary>
    public required bool Retryable { get; init; }

    /// <summary>Additional safe diagnostic details, or <see langword="null"/> when none exist.</summary>
    public string? Details { get; init; }
}
