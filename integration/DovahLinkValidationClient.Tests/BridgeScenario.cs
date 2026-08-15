using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Shared connection setup and teardown for integration scenarios.</summary>
public static class BridgeScenario
{
    /// <summary>A valid one-time token used by the deterministic harness scenarios.</summary>
    public const string ValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

    /// <summary>The loopback endpoint exposed by the deterministic bridge harness.</summary>
    public static readonly Uri BridgeUri = new("ws://127.0.0.1:58231/");

    /// <summary>
    /// Builds a hello envelope for client authentication.
    /// </summary>
    /// <param name="token">The one-time local authentication token.</param>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <param name="clientId">The logical client identity to offer, required by the bridge in every hello.</param>
    /// <returns>A hello envelope containing the client endpoint and authentication details.</returns>
    public static Envelope HelloEnvelope(string token, string messageId = "message-hello-1", string clientId = "client-1")
    {
        var payload = new JsonObject
        {
            ["endpoint"] = "client",
            ["clientId"] = clientId,
            ["auth"] = new JsonObject
            {
                ["method"] = "one_time_local_token",
                ["token"] = token,
            },
        };
        return new Envelope("hello", messageId, null, null, payload);
    }

    /// <summary>
    /// Establishes an authenticated bridge connection and retrieves its session and capabilities.
    /// </summary>
    /// <returns>
    /// The harness process, bridge connection, assigned session ID, and capabilities envelope.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the harness does not become ready or the bridge handshake does not produce the expected messages.
    /// </exception>
    public static Task<(HarnessProcess Harness, BridgeConnection Connection, string SessionId, Envelope Capabilities)> ConnectAndAuthenticateAsync()
    {
        return ConnectAndAuthenticateAsync(ValidHexToken, connectionFactory: null);
    }

    /// <summary>Establishes a connection using a selectable hello token for failure-path scenarios.</summary>
    /// <param name="helloToken">The token sent in the client hello.</param>
    /// <param name="connectionFactory">An optional deterministic connection source for setup-failure tests.</param>
    /// <returns>The authenticated harness resources and negotiated values.</returns>
    internal static async Task<(HarnessProcess Harness, BridgeConnection Connection, string SessionId, Envelope Capabilities)> ConnectAndAuthenticateAsync(
        string helloToken,
        Func<Task<BridgeConnection>>? connectionFactory = null)
    {
        var harness = new HarnessProcess(ValidHexToken);
        BridgeConnection? connection = null;
        try
        {
            await harness.WaitForReadyAsync();
            // A capture has nowhere to be attributed to before a play context
            // exists (ActivePlayContextLevelSink drops it, matching real
            // play: main menu has no play context). Begin one here so every
            // scenario using this shared setup sees a real, non-"unavailable"
            // character state, not because of anything specific to
            // authentication or capabilities.
            await harness.WriteLineAsync("new_game");
            await harness.ReadLineAsync();  // PLAY_CONTEXT <id>

            connection = connectionFactory is null
                ? await BridgeConnection.ConnectWithRetryAsync(BridgeUri)
                : await connectionFactory();
            await connection.SendAsync(HelloEnvelope(helloToken));

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
        catch
        {
            try
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
            finally
            {
                harness.Dispose();
            }
            throw;
        }
    }

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
