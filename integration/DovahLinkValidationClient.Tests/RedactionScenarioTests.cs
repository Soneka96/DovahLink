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

        await connection.SendAsync(new Envelope(1, "ping", "message-redact-3", sessionId + "-foreign", null, new JsonObject()));
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

    /// <summary>Serializes an envelope into the text inspected by the redaction test.</summary>
    private static string EnvelopeText(Envelope envelope) =>
        $"{envelope.MessageType}|{envelope.MessageId}|{envelope.SessionId}|{envelope.CorrelationId}|{envelope.Payload.ToJsonString()}";
}
