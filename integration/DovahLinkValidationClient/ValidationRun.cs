using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Drives one validation-client connection through the handshake, Bridge-version compatibility
/// check, and initial subscription. Extracted from <c>Program.cs</c>'s top-level script so its
/// outcome -- including the incompatible-version rejection path -- can be exercised by tests.
/// </summary>
public static class ValidationRun
{
    /// <summary>
    /// Authenticates over an already-connected socket, evaluates the Bridge's reported version, and
    /// -- only when supported -- exchanges capabilities and subscribes to character state.
    /// </summary>
    /// <param name="connection">The connection to drive; the caller owns connecting and disposal.</param>
    /// <param name="token">The one-time bridge authentication token.</param>
    /// <param name="output">Destination for progress and diagnostic messages.</param>
    /// <returns>0 when an initial state snapshot was received; 1 on any failure, including a
    /// rejected hello, an unaccepted client identity, an incompatible Bridge version, or a
    /// connection or protocol failure.</returns>
    public static async Task<int> ExecuteAsync(BridgeConnection connection, string token, TextWriter output)
    {
        string clientId = ClientIdentity.Current.ToString();
        try
        {
            var helloPayload = new JsonObject
            {
                ["endpoint"] = "client",
                ["clientId"] = clientId,
                ["auth"] = new JsonObject
                {
                    ["method"] = "one_time_local_token",
                    ["token"] = token,
                },
            };
            await connection.SendAsync(new Envelope("hello", Guid.NewGuid().ToString(), null, null, helloPayload));

            Envelope helloAck = await connection.ReceiveAsync();
            if (helloAck.MessageType != "hello_ack")
            {
                output.WriteLine($"Bridge rejected hello: {helloAck.MessageType} {helloAck.Payload}");
                return 1;
            }
            if (helloAck.ClientId != clientId)
            {
                output.WriteLine(
                    $"Bridge accepted a different client identity than requested: expected {clientId}, " +
                    $"got {helloAck.ClientId ?? "null"}.");
                await connection.CloseAsync();
                return 1;
            }
            string sessionId = helloAck.SessionId ?? throw new InvalidOperationException("hello_ack carried no sessionId.");
            string? bridgeVersion = helloAck.Payload["bridgeVersion"]?.GetValue<string>();

            // The Bridge does not evaluate this itself and does not reject the connection on
            // compatibility grounds (protocol/schema/README.md): this client fails explicitly on its
            // own side, before continuing the exchange, rather than risk misinterpreting an
            // incompatible release's messages.
            BridgeVersionCompatibilityResult compatibility = BridgeVersionCompatibility.Evaluate(bridgeVersion);
            if (compatibility != BridgeVersionCompatibilityResult.Supported)
            {
                output.WriteLine(
                    $"Bridge version '{bridgeVersion}' is incompatible ({compatibility}); this client supports " +
                    $"{BridgeVersionCompatibility.MinimumSupportedVersion} to " +
                    $"{BridgeVersionCompatibility.MaximumSupportedVersion}.");
                await connection.CloseAsync();
                return 1;
            }
            output.WriteLine($"hello_ack: sessionId={sessionId} bridgeVersion={bridgeVersion}");

            Envelope capabilities = await connection.ReceiveAsync();
            output.WriteLine($"capabilities: {capabilities.Payload}");

            var subscribePayload = new JsonObject { ["stateAreas"] = new JsonArray("character") };
            await connection.SendAsync(new Envelope("subscribe", Guid.NewGuid().ToString(), sessionId, null, subscribePayload));

            Envelope subscriptionAck = await connection.ReceiveAsync();
            output.WriteLine($"subscription_ack: {subscriptionAck.Payload}");

            Envelope snapshot = await connection.ReceiveAsync();
            output.WriteLine($"state_snapshot: {snapshot.Payload}");

            return 0;
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidOperationException
            or FormatException or OperationCanceledException)
        {
            output.WriteLine($"Bridge connection failed: {ex.Message}");
            return 1;
        }
    }
}
