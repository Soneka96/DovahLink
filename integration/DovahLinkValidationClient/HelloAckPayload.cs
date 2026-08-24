using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>hello_ack</c> payload (<c>protocol/schema/README.md</c>'s <c>hello_ack</c>).
/// Decode-only: this client never sends <c>hello_ack</c>.
/// </summary>
/// <param name="BridgeVersion">The DovahLink Bridge/mod release version
/// (<c>ai/context/protocol/compatibility.md</c>'s compatibility bootstrap).</param>
/// <param name="ClientIdentityKind">The wire vocabulary of <c>hello_ack.clientIdentityKind</c>:
/// <c>"unpaired"</c> or <c>"paired"</c>.</param>
public sealed record HelloAckPayload(string BridgeVersion, string ClientIdentityKind)
{
    /// <summary>
    /// Decodes and validates one <c>hello_ack</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded hello-ack payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing or the wrong JSON
    /// type.</exception>
    public static HelloAckPayload Decode(JsonObject payload)
    {
        try
        {
            string bridgeVersion = payload["bridgeVersion"]?.GetValue<string>()
                ?? throw new FormatException("Missing bridgeVersion.");
            string clientIdentityKind = payload["clientIdentityKind"]?.GetValue<string>()
                ?? throw new FormatException("Missing clientIdentityKind.");
            return new HelloAckPayload(bridgeVersion, clientIdentityKind);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed hello_ack payload: {ex.Message}", ex);
        }
    }
}
