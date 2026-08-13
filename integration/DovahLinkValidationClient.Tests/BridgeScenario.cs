using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

// Shared setup every scenario test needs: launch the harness, connect, and
// complete the hello/hello_ack/capabilities handshake up to the point a
// scenario actually wants to test something. Kept in one place so each
// scenario file only states what's different about it, matching this
// project's own established preference (see bridge/README.md's
// WebSocketSession::SetReadTimeout note) for one reusable place to update
// instead of duplicating a sequence across every test file.
public static class BridgeScenario
{
    public const string ValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";
    public static readonly Uri BridgeUri = new("ws://127.0.0.1:58231/");

    /// <summary>
    /// Builds a version-0 hello envelope for client authentication.
    /// </summary>
    /// <param name="token">The one-time local authentication token.</param>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <param name="supportedProtocolVersions">The protocol versions supported by the client; defaults to version 1.</param>
    /// <returns>A hello envelope containing the client endpoint, supported protocol versions, and authentication details.</returns>
    public static Envelope HelloEnvelope(
        string token, string messageId = "message-hello-1", int[]? supportedProtocolVersions = null)
    {
        var payload = new JsonObject
        {
            ["endpoint"] = "client",
            ["supportedProtocolVersions"] = new JsonArray((supportedProtocolVersions ?? [1]).Select(v => (JsonNode)v).ToArray()),
            ["auth"] = new JsonObject
            {
                ["method"] = "one_time_local_token",
                ["token"] = token,
            },
        };
        return new Envelope(0, "hello", messageId, null, null, payload);
    }

    // Launches a harness, waits for READY, connects, and completes hello
    // through the bridge's own outbound capabilities message. Returns the
    // open connection, the session ID the bridge issued, and that
    // capabilities envelope itself (its content is asserted by
    /// <summary>
    /// Establishes an authenticated bridge connection and retrieves its session and capabilities.
    /// </summary>
    /// <returns>
    /// The harness process, bridge connection, assigned session ID, and capabilities envelope.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the harness does not become ready or the bridge handshake does not produce the expected messages.
    /// </exception>
    public static async Task<(HarnessProcess Harness, BridgeConnection Connection, string SessionId, Envelope Capabilities)> ConnectAndAuthenticateAsync()
    {
        var harness = new HarnessProcess(ValidHexToken);
        string? ready = await harness.ReadLineAsync();
        if (ready != "READY")
        {
            throw new InvalidOperationException($"Harness did not report READY: {ready}. Stderr: {harness.StandardError}");
        }

        BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri);
        await connection.SendAsync(HelloEnvelope(ValidHexToken));

        Envelope helloAck = await connection.ReceiveAsync();
        if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null)
        {
            throw new InvalidOperationException($"Expected hello_ack with a sessionId, got {helloAck.MessageType}: {helloAck.Payload}");
        }

        Envelope capabilities = await connection.ReceiveAsync();
        if (capabilities.MessageType != "capabilities")
        {
            throw new InvalidOperationException($"Expected the bridge's own capabilities message, got {capabilities.MessageType}.");
        }

        return (harness, connection, helloAck.SessionId, capabilities);
    }

    // Closes the connection (so Coordinator::Shutdown doesn't have to wait
    // out the idle timeout for it -- bridge/README.md), tells the harness to
    // quit, and confirms it exits cleanly. The common teardown every
    /// <summary>
    /// Closes the bridge connection and requests clean harness termination.
    /// </summary>
    /// <param name="harness">The harness process to stop.</param>
    /// <param name="connection">The bridge connection to close.</param>
    /// <exception cref="TimeoutException">Thrown if the harness does not exit within five seconds.</exception>
    public static async Task CloseAndQuitAsync(HarnessProcess harness, BridgeConnection connection)
    {
        await connection.CloseAsync();
        await harness.WriteLineAsync("quit");
        if (!await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Harness did not exit after quit. Stderr: {harness.StandardError}");
        }
    }

    // Runs `count` separate failed-token connection attempts against
    // `harness`, one after another (FailedTokenThrottle is one instance
    // shared across the harness's whole lifetime, not per-connection --
    // security/throttle.hpp -- so this is what "N recent failures" means).
    // Each connection is fully torn down before the next, since a failed
    // hello always closes the connection (handshake_handler.cpp's design
    /// <summary>
    /// Records sequential failed authentication attempts using separate bridge connections.
    /// </summary>
    /// <param name="count">The number of failed authentication attempts to perform.</param>
    /// <param name="wrongButValidHexToken">A token with valid hexadecimal formatting that is rejected for authentication.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task RecordFailedTokenAttemptsAsync(int count, string wrongButValidHexToken)
    {
        for (int attempt = 1; attempt <= count; attempt++)
        {
            await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri);
            await connection.SendAsync(HelloEnvelope(wrongButValidHexToken, $"message-hello-fail-{attempt}"));
            await connection.ReceiveAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
            await connection.CloseAsync();
        }
    }
}
