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
    /// Builds an unpaired hello envelope: no credential presented yet, admitting a session
    /// restricted to ping/capabilities/pairing_request/pairing_confirm/pairing_ack until pairing
    /// succeeds (protocol/schema/README.md's <c>hello</c> subsection).
    /// </summary>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <param name="clientId">The logical client identity to offer, required by the bridge in every hello.</param>
    /// <returns>A hello envelope with <c>auth.method: "unpaired"</c> and no <c>auth.token</c> field.</returns>
    public static Envelope UnpairedHelloEnvelope(string messageId = "message-hello-1", string clientId = "client-1")
    {
        var payload = new JsonObject
        {
            ["endpoint"] = "client",
            ["clientId"] = clientId,
            ["auth"] = new JsonObject
            {
                ["method"] = "unpaired",
            },
        };
        return new Envelope("hello", messageId, null, null, payload);
    }

    /// <summary>
    /// Builds a trusted-device-credential hello envelope for an ordinary reconnect after pairing.
    /// </summary>
    /// <param name="credential">The hex-encoded credential issued by a prior successful pairing.</param>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <param name="clientId">The logical client identity to offer, required by the bridge in every hello.</param>
    /// <returns>A hello envelope with <c>auth.method: "trusted_device_credential"</c>.</returns>
    public static Envelope TrustedDeviceHelloEnvelope(string credential, string messageId = "message-hello-1", string clientId = "client-1")
    {
        var payload = new JsonObject
        {
            ["endpoint"] = "client",
            ["clientId"] = clientId,
            ["auth"] = new JsonObject
            {
                ["method"] = "trusted_device_credential",
                ["token"] = credential,
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
            harness.PlayContextId = await ReadPlayContextReportAsync(harness);

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
    /// Establishes a Restricted-tier bridge connection authenticated via the <c>unpaired</c> hello
    /// method, for pairing scenarios. Unlike <see cref="ConnectAndAuthenticateAsync()"/>, callers
    /// typically supply <paramref name="extraEnvironmentVariables"/> to isolate the harness's trust
    /// store (<c>DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE</c>, bridge/README.md) so a pairing
    /// scenario never reads or writes the developer's real per-user trust store.
    /// </summary>
    /// <param name="extraEnvironmentVariables">Additional environment variables for the harness.</param>
    /// <returns>
    /// The harness process, bridge connection, assigned session ID, hello acknowledgment, and capabilities envelope.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the harness does not become ready or the bridge handshake does not produce the expected messages.
    /// </exception>
    public static async Task<(HarnessProcess Harness, BridgeConnection Connection, string SessionId, Envelope HelloAck, Envelope Capabilities)> ConnectAndAuthenticateUnpairedAsync(
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null)
    {
        var harness = new HarnessProcess(ValidHexToken, extraEnvironmentVariables);
        BridgeConnection? connection = null;
        try
        {
            await harness.WaitForReadyAsync();
            // See ConnectAndAuthenticateAsync's matching comment: a play context gives every
            // scenario using this shared setup a real, non-"unavailable" character state.
            await harness.WriteLineAsync("new_game");
            harness.PlayContextId = await ReadPlayContextReportAsync(harness);

            connection = await BridgeConnection.ConnectWithRetryAsync(BridgeUri);
            await connection.SendAsync(UnpairedHelloEnvelope());

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

            return (harness, connection, helloAck.SessionId, helloAck, capabilities);
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
    /// Reads and validates the harness's <c>PAIRING_CODE</c> line, printed by
    /// <c>StdoutPairingNotificationSink</c> (bridge/harness/dovahlink_bridge_harness.cpp) the moment
    /// a fresh pairing challenge starts.
    /// </summary>
    /// <param name="harness">The harness process to read the report from.</param>
    /// <returns>The six-digit pairing code.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the harness does not report a well-formed, non-empty pairing code.
    /// </exception>
    public static async Task<string> ReadPairingCodeReportAsync(HarnessProcess harness)
    {
        const string pairingCodePrefix = "PAIRING_CODE ";
        string? pairingCodeLine = await harness.ReadLineAsync();
        if (pairingCodeLine is null || !pairingCodeLine.StartsWith(pairingCodePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Harness did not report a pairing code: {pairingCodeLine}. Stderr: {harness.StandardError}");
        }

        string code = pairingCodeLine[pairingCodePrefix.Length..];
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException(
                $"Harness reported an empty pairing code: {pairingCodeLine}. Stderr: {harness.StandardError}");
        }

        return code;
    }

    /// <summary>
    /// Reads and validates the harness's <c>new_game</c> play-context report line.
    /// </summary>
    /// <param name="harness">The harness process to read the report from.</param>
    /// <returns>The reported play context ID.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the harness does not report a well-formed, non-empty play context ID.
    /// </exception>
    public static async Task<string> ReadPlayContextReportAsync(HarnessProcess harness)
    {
        const string playContextPrefix = "PLAY_CONTEXT ";
        string? playContextLine = await harness.ReadLineAsync();
        if (playContextLine is null || !playContextLine.StartsWith(playContextPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Harness did not report a play context for 'new_game': {playContextLine}. Stderr: {harness.StandardError}");
        }

        string playContextId = playContextLine[playContextPrefix.Length..];
        if (string.IsNullOrWhiteSpace(playContextId))
        {
            throw new InvalidOperationException(
                $"Harness reported an empty play context ID for 'new_game': {playContextLine}. Stderr: {harness.StandardError}");
        }

        return playContextId;
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
    /// Builds a pairing-renotify request envelope to redisplay the active challenge's code.
    /// </summary>
    /// <param name="sessionId">The authenticated session ID.</param>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <returns>A pairing_renotify envelope with no payload, matching protocol/schema/README.md.</returns>
    public static Envelope RenotifyEnvelope(string sessionId, string messageId = "message-renotify-1")
    {
        return new Envelope("pairing_renotify", messageId, sessionId, null, new JsonObject());
    }

    /// <summary>
    /// Builds a pairing-cancel request envelope to clear an owned active challenge or pending credential.
    /// </summary>
    /// <param name="sessionId">The authenticated session ID.</param>
    /// <param name="messageId">The message identifier for the envelope.</param>
    /// <returns>A pairing_cancel envelope with no payload, matching protocol/schema/README.md.</returns>
    public static Envelope CancelEnvelope(string sessionId, string messageId = "message-cancel-1")
    {
        return new Envelope("pairing_cancel", messageId, sessionId, null, new JsonObject());
    }

    /// <summary>Builds a harness environment override isolating the trust store at <paramref name="path"/>.</summary>
    /// <param name="path">The isolated trust-store file path.</param>
    public static Dictionary<string, string> TrustStoreOverride(string path) =>
        new() { ["DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE"] = path };

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
