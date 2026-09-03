namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>rename_request</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>rename_request</c>" section. Full-session only.
/// </summary>
public sealed record RenameRequestPayload
{
    /// <summary>The new display name. An empty value clears the device's display name.</summary>
    public required string DisplayName { get; init; }
}
