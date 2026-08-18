using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Verifies that protocol and harness diagnostics redact sensitive details.</summary>
public class RedactionScenarioTests
{
    /// <summary>The token that must never appear in observed output.</summary>
    private const string SecretToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

    /// <summary>A second token that must never appear in observed output.</summary>
    private const string WrongToken = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    /// <summary>Patterns representing secrets, paths, and raw infrastructure details.</summary>
    private static readonly string[] ForbiddenPatterns =
    [
        SecretToken,
        WrongToken,
        @"C:\",                // a Windows filesystem path
        "bridge\\",            // a repo-relative path fragment
        "bridge/",
        ".cpp",
        ".hpp",
        "Exception",
        "exception",
        "std::",
        "boost::",
        "0x",                  // a raw pointer/address in an exception message
    ];

    /// <summary>Verifies that responses and harness output contain no sensitive details.</summary>
    [Fact]
    public async Task NoResponseOrHarnessOutputEverContainsASecretPathOrRawException()
    {
        var collectedText = new List<string>();

        using var harness = new HarnessProcess(SecretToken);
        await harness.WaitForReadyAsync();

        // 1. A failed hello with a distinctive wrong token.
        await using (BridgeConnection failedHello = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await failedHello.SendAsync(BridgeScenario.HelloEnvelope(WrongToken, "message-redact-1"));
            Envelope error = await failedHello.ReceiveAsync();
            collectedText.Add(EnvelopeText(error));
            await failedHello.CloseAsync();
        }

        // 2. A successful session, then two representative failure paths
        // that could plausibly carry leftover diagnostic detail: malformed
        // JSON and a stale session. Deliberately only two: each is a
        // protocol violation, and a third would close the connection
        // before its response could be collected (ai/context/protocol/
        // security.md's 3-in-30s limit, confirmed directly by
        // LimitsScenarioTests.cs's DifferentViolationTypesAccumulate... --
        // this test only needs a representative sample, not exhaustive
        // coverage of every failure code, which the other scenario files
        // already provide).
        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(SecretToken, "message-redact-2"));
        Envelope helloAck = await connection.ReceiveAsync();
        string sessionId = helloAck.SessionId!;
        collectedText.Add(EnvelopeText(helloAck));
        Envelope bridgeCapabilities = await connection.ReceiveAsync();
        collectedText.Add(EnvelopeText(bridgeCapabilities));

        await connection.SendRawTextAsync("not json {{{");
        collectedText.Add(EnvelopeText(await connection.ReceiveAsync()));

        await connection.SendAsync(new Envelope("ping", "message-redact-3", sessionId + "-foreign", null, new JsonObject()));
        collectedText.Add(EnvelopeText(await connection.ReceiveAsync()));

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
        collectedText.Add(harness.StandardError);

        foreach (string text in collectedText)
        {
            foreach (string forbidden in ForbiddenPatterns)
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Verifies that a real pairing code and issued credential never appear anywhere except the
    /// owning connection's own designated response. The bridge never echoes a pairing code back to
    /// anyone by protocol design, so that check is unconditional; the credential is legitimately
    /// returned once, inside the owning connection's own <c>pairing_outcome</c>, so this test
    /// captures that connection's traffic separately and scans only genuinely separate activity
    /// afterward -- a different connection/client, a malformed message on it, an admin command's
    /// reply, and harness stderr -- for a coincidental leak. Only response <em>payloads</em> are
    /// scanned, not the full envelope text: `messageId`/`sessionId` are long opaque random IDs that
    /// could coincidentally contain six consecutive digits matching the short numeric code, unlike
    /// the credential's own far longer hex string, so including them would risk an unrelated,
    /// flaky failure the way it would not for the original token-focused test above.
    /// </summary>
    [Fact]
    public async Task PairingSecretsNeverAppearOutsideTheirDesignatedResponse()
    {
        var unrelatedPayloads = new List<string>();
        using var trustStore = new IsolatedTrustStore();

        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken, trustStore.Override());
        await harness.WaitForReadyAsync();

        string code;
        string credential;
        await using (BridgeConnection pairing = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await pairing.SendAsync(BridgeScenario.UnpairedHelloEnvelope());
            Envelope helloAck = await pairing.ReceiveAsync();
            if (helloAck.MessageType != "hello_ack" || helloAck.SessionId is null)
            {
                throw new InvalidOperationException($"Expected hello_ack with a sessionId, got {helloAck.MessageType}: {helloAck.Payload}");
            }
            string sessionId = helloAck.SessionId;
            await pairing.ReceiveAsync();  // capabilities

            await pairing.SendAsync(new Envelope("pairing_request", "message-request-1", sessionId, null, new JsonObject()));
            await pairing.ReceiveAsync();  // pairing_status

            code = await BridgeScenario.ReadPairingCodeReportAsync(harness);

            await pairing.SendAsync(new Envelope("pairing_confirm", "message-confirm-1", sessionId, null,
                new JsonObject { ["code"] = code }));
            Envelope confirmOutcome = await pairing.ReceiveAsync();
            if (confirmOutcome.Payload["outcome"]?.GetValue<string>() != "credential_issued")
            {
                throw new InvalidOperationException($"Expected credential_issued, got {confirmOutcome.Payload}.");
            }
            credential = confirmOutcome.Payload["credential"]!.GetValue<string>();

            await pairing.SendAsync(new Envelope("pairing_ack", "message-ack-1", sessionId, null,
                new JsonObject { ["credential"] = credential }));
            Envelope ackOutcome = await pairing.ReceiveAsync();
            if (ackOutcome.Payload["outcome"]?.GetValue<string>() != "trusted")
            {
                throw new InvalidOperationException($"Expected trusted, got {ackOutcome.Payload}.");
            }

            await pairing.CloseAsync();
        }

        // Genuinely separate activity: a different connection and client entirely, a malformed
        // message on it, and an admin command's reply -- none of this has any legitimate reason to
        // carry the code or credential from the pairing above.
        await using (BridgeConnection unrelated = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await unrelated.SendAsync(BridgeScenario.UnpairedHelloEnvelope(clientId: "client-2"));
            unrelatedPayloads.Add((await unrelated.ReceiveAsync()).Payload.ToJsonString());
            unrelatedPayloads.Add((await unrelated.ReceiveAsync()).Payload.ToJsonString());  // capabilities

            await unrelated.SendRawTextAsync("not json {{{");
            unrelatedPayloads.Add((await unrelated.ReceiveAsync()).Payload.ToJsonString());
            await unrelated.CloseAsync();
        }

        await harness.WriteLineAsync("revoke client-1");
        unrelatedPayloads.Add(await harness.ReadLineAsync() ?? string.Empty);

        await harness.WriteLineAsync("quit");
        await harness.WaitForExitAsync(TimeSpan.FromSeconds(5));
        unrelatedPayloads.Add(harness.StandardError);

        foreach (string text in unrelatedPayloads)
        {
            Assert.DoesNotContain(code, text, StringComparison.Ordinal);
            Assert.DoesNotContain(credential, text, StringComparison.Ordinal);
        }
    }

    /// <summary>Serializes an envelope into the text inspected by the redaction test.</summary>
    private static string EnvelopeText(Envelope envelope) =>
        $"{envelope.MessageType}|{envelope.MessageId}|{envelope.SessionId}|{envelope.CorrelationId}|{envelope.Payload.ToJsonString()}";
}
