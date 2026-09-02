namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>hello</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>hello</c>" section.
/// The connecting client always sends this first; the host never initiates a connection or sends
/// <c>hello</c> itself.
/// </summary>
public sealed record HelloPayload
{
    /// <summary>The sender's role, always <c>"client"</c>.</summary>
    public required string Endpoint { get; init; }

    /// <summary>The logical client/installation identity, persistent across reconnects and not itself a trust credential.</summary>
    public required string ClientId { get; init; }

    /// <summary>The presented authentication method and credential.</summary>
    public required HelloAuthPayload Auth { get; init; }
}
