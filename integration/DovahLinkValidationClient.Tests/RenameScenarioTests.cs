using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Proves the authenticated rename request/outcome contract against the real bridge harness.</summary>
public class RenameScenarioTests
{
    /// <summary>Verifies that an authenticated device can replace its display name.</summary>
    [Fact]
    public async Task AuthenticatedDeviceCanRenameItself()
    {
        var scenario = await PairTrustedAsync("Before");
        using var disposeTrustStore = scenario.TrustStore;
        using var disposeHarness = scenario.Harness;
        await using var disposeConnection = scenario.Connection;

        Envelope outcome = await SendRenameAsync(scenario.Connection, scenario.SessionId, "rename-1", "Renamed");

        AssertRenamedOutcome(outcome, scenario.Harness, scenario.SessionId, "rename-1", "Renamed");
        await BridgeScenario.CloseAndQuitAsync(scenario.Harness, scenario.Connection);
    }

    /// <summary>Verifies that an empty authenticated rename clears the display name.</summary>
    [Fact]
    public async Task AuthenticatedDeviceCanClearItsDisplayName()
    {
        var scenario = await PairTrustedAsync("Before");
        using var disposeTrustStore = scenario.TrustStore;
        using var disposeHarness = scenario.Harness;
        await using var disposeConnection = scenario.Connection;

        Envelope outcome = await SendRenameAsync(scenario.Connection, scenario.SessionId, "rename-clear", string.Empty);

        AssertBridgeResponse(outcome, scenario.Harness, "rename_outcome", scenario.SessionId, "rename-clear");
        RenameOutcomePayload decoded = RenameOutcomePayload.Decode(outcome.Payload);
        Assert.Equal("renamed", decoded.Outcome);
        Assert.Null(decoded.DisplayName);
        await BridgeScenario.CloseAndQuitAsync(scenario.Harness, scenario.Connection);
    }

    /// <summary>Verifies that an invalid display name returns the documented graceful outcome.</summary>
    [Fact]
    public async Task InvalidDisplayNameReturnsAnInvalidDisplayNameOutcome()
    {
        var scenario = await PairTrustedAsync("Before");
        using var disposeTrustStore = scenario.TrustStore;
        using var disposeHarness = scenario.Harness;
        await using var disposeConnection = scenario.Connection;

        Envelope outcome = await SendRenameAsync(scenario.Connection, scenario.SessionId, "rename-invalid", new string('x', 65));

        AssertBridgeResponse(outcome, scenario.Harness, "rename_outcome", scenario.SessionId, "rename-invalid");
        RenameOutcomePayload decoded = RenameOutcomePayload.Decode(outcome.Payload);
        Assert.Equal("invalid_display_name", decoded.Outcome);
        Assert.Null(decoded.DisplayName);
        await BridgeScenario.CloseAndQuitAsync(scenario.Harness, scenario.Connection);
    }

    /// <summary>Verifies that a restricted unpaired session cannot send a rename request.</summary>
    [Fact]
    public async Task RestrictedSessionRejectsRenameRequest()
    {
        using var trustStore = new IsolatedTrustStore();
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(new Envelope("rename_request", "rename-restricted", sessionId, null,
            new RenameRequestPayload("Should Fail").Encode()));
        Envelope error = await connection.ReceiveAsync();

        AssertBridgeResponse(error, harness, "error", sessionId, "rename-restricted");
        Assert.Equal("malformed_message", ErrorPayload.Decode(error.Payload).Code);
        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>
    /// Completes the real pairing handshake and returns the still-open trusted session used by a
    /// rename scenario.
    /// </summary>
    /// <param name="displayName">The initial display name supplied during pairing.</param>
    /// <returns>The isolated trust store, harness, trusted connection, and session ID.</returns>
    private static async Task<(IsolatedTrustStore TrustStore, HarnessProcess Harness, BridgeConnection Connection, string SessionId)> PairTrustedAsync(
        string displayName)
    {
        var trustStore = new IsolatedTrustStore();
        HarnessProcess? harness = null;
        BridgeConnection? connection = null;
        try
        {
            var setup = await BridgeScenario.ConnectAndAuthenticateUnpairedAsync(trustStore.Override());
            harness = setup.Harness;
            connection = setup.Connection;

            await connection.SendAsync(new Envelope("pairing_request", "pairing-request", setup.SessionId, null,
                new JsonObject()));
            Envelope status = await connection.ReceiveAsync();
            AssertBridgeResponse(status, setup.Harness, "pairing_status", setup.SessionId, "pairing-request");
            Assert.Equal("available", PairingStatusPayload.Decode(status.Payload).State);

            string code = await BridgeScenario.ReadPairingCodeReportAsync(harness);
            await connection.SendAsync(new Envelope("pairing_confirm", "pairing-confirm", setup.SessionId, null,
                new PairingConfirmPayload(code, displayName).Encode()));
            Envelope confirmOutcome = await connection.ReceiveAsync();
            AssertBridgeResponse(confirmOutcome, setup.Harness, "pairing_outcome", setup.SessionId, "pairing-confirm");
            PairingOutcomePayload confirmDecoded = PairingOutcomePayload.Decode(confirmOutcome.Payload);
            Assert.Equal("credential_issued", confirmDecoded.Outcome);
            string credential = confirmDecoded.Credential!;

            await connection.SendAsync(new Envelope("pairing_ack", "pairing-ack", setup.SessionId, null,
                new PairingAckPayload(credential).Encode()));
            Envelope ackOutcome = await connection.ReceiveAsync();
            AssertBridgeResponse(ackOutcome, setup.Harness, "pairing_outcome", setup.SessionId, "pairing-ack");
            Assert.Equal("trusted", PairingOutcomePayload.Decode(ackOutcome.Payload).Outcome);

            return (trustStore, harness, connection, setup.SessionId);
        }
        catch
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
            harness?.Dispose();
            trustStore.Dispose();
            throw;
        }
    }

    /// <summary>Sends one rename request on an authenticated session.</summary>
    /// <param name="connection">The active bridge connection.</param>
    /// <param name="sessionId">The session ID established by hello.</param>
    /// <param name="messageId">The request message ID used for response correlation.</param>
    /// <param name="displayName">The required display-name string, possibly empty.</param>
    /// <returns>The bridge's rename outcome or error envelope.</returns>
    private static async Task<Envelope> SendRenameAsync(BridgeConnection connection, string sessionId,
                                                         string messageId, string displayName)
    {
        await connection.SendAsync(new Envelope("rename_request", messageId, sessionId, null,
            new RenameRequestPayload(displayName).Encode()));
        return await connection.ReceiveAsync();
    }

    /// <summary>Asserts the successful non-empty rename outcome and its envelope metadata.</summary>
    /// <param name="outcome">The received bridge envelope.</param>
    /// <param name="harness">The harness that reported the expected bridge and play-context IDs.</param>
    /// <param name="sessionId">The expected session ID.</param>
    /// <param name="requestMessageId">The request ID expected in correlationId.</param>
    /// <param name="displayName">The expected returned display name.</param>
    private static void AssertRenamedOutcome(Envelope outcome, HarnessProcess harness, string sessionId,
                                             string requestMessageId, string displayName)
    {
        AssertBridgeResponse(outcome, harness, "rename_outcome", sessionId, requestMessageId);
        RenameOutcomePayload decoded = RenameOutcomePayload.Decode(outcome.Payload);
        Assert.Equal("renamed", decoded.Outcome);
        Assert.Equal(displayName, decoded.DisplayName);
    }

    /// <summary>Asserts the common response envelope fields for a post-handshake bridge message.</summary>
    /// <param name="response">The received bridge envelope.</param>
    /// <param name="harness">The harness that reported the expected bridge and play-context IDs.</param>
    /// <param name="messageType">The expected response message type.</param>
    /// <param name="sessionId">The expected session identifier.</param>
    /// <param name="correlationId">The request message ID expected in correlationId.</param>
    private static void AssertBridgeResponse(Envelope response, HarnessProcess harness, string messageType,
                                             string sessionId, string correlationId)
    {
        Assert.Equal(messageType, response.MessageType);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal(correlationId, response.CorrelationId);
        Assert.NotNull(harness.BridgeInstanceId);
        Assert.NotNull(harness.PlayContextId);
        Assert.Equal(harness.BridgeInstanceId, response.BridgeInstanceId);
        Assert.Equal(harness.PlayContextId, response.PlayContextId);
        Assert.Null(response.ClientId);
    }
}
