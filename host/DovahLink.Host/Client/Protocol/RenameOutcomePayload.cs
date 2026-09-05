namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>rename_outcome</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>rename_outcome</c>" section. Host-originated reply to <c>rename_request</c>.
/// </summary>
public sealed record RenameOutcomePayload
{
    /// <summary>The rename result.</summary>
    public required RenameOutcomeWireValue Outcome { get; init; }

    /// <summary>The resulting display name, present only for <c>renamed</c>; <see langword="null"/> when the rename cleared the name or for any other outcome.</summary>
    public string? DisplayName { get; init; }
}
