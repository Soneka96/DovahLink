using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises protocol size, shape, violation, and rate limits.</summary>
public class LimitsScenarioTests
{
    /// <summary>The maximum inbound WebSocket frame size.</summary>
    private const int MaxInboundFrameBytes = 1024 * 1024;

    /// <summary>The maximum permitted JSON nesting depth.</summary>
    private const int MaxJsonNestingDepth = 32;

    /// <summary>The maximum permitted string length in bytes.</summary>
    private const int MaxStringLengthBytes = 4 * 1024;

    /// <summary>The maximum permitted array item count.</summary>
    private const int MaxArrayItems = 128;

    /// <summary>The maximum permitted object member count.</summary>
    private const int MaxObjectMembers = 64;

    /// <summary>Wraps raw JSON payload text in a protocol envelope.</summary>
    private static string EnvelopeWithRawPayload(string sessionId, string rawPayloadJson, string messageId = "message-limit-1") =>
        $$"""{"messageType": "ping", "messageId": "{{messageId}}", "sessionId": "{{sessionId}}", "correlationId": null, "payload": {{rawPayloadJson}}, "bridgeInstanceId": null, "playContextId": null, "clientId": null}""";

    /// <summary>Verifies that invalid JSON is rejected as a malformed message.</summary>
    [Fact]
    public async Task TextThatIsNotValidJsonIsRejectedAsMalformedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendRawTextAsync("not json at all {{{");

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());
        Assert.False(error.Payload["retryable"]!.GetValue<bool>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an oversized frame closes the connection without a response.</summary>
    [Fact]
    public async Task FrameLargerThanTheLimitClosesTheConnectionWithNoResponse()
    {
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Doesn't need to be valid JSON: both enforcement layers (Beast's
        // own frame-size cap and ParseBoundedJson's byte-count check,
        // websocket_session.cpp / bounded_json.cpp) reject purely on size,
        // before any parsing is attempted.
        string oversized = new string('a', MaxInboundFrameBytes + 1);
        await connection.SendRawTextAsync(oversized);

        // "Close immediately... do not attempt to send an error over an
        // invalid or oversized frame" (ai/context/protocol/security.md) --
        // no response at all, unlike every other violation in this file.
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        await connection.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that excessive JSON nesting is rejected.</summary>
    [Fact]
    public async Task NestingDeeperThanTheLimitIsRejectedAsMalformedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        // Boost.JSON's own parser enforces max_depth and fails the parse
        // outright (bounded_json.cpp's comment: this has no dedicated
        // error code of its own, it surfaces as kInvalidJson / malformed_message).
        int depth = MaxJsonNestingDepth + 10;
        string deeplyNested = new string('[', depth) + "1" + new string(']', depth);
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(sessionId, $$"""{"deep": {{deeplyNested}}}"""));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an oversized string value is rejected.</summary>
    [Fact]
    public async Task StringLongerThanTheLimitIsRejectedAsMalformedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string tooLong = new string('a', MaxStringLengthBytes + 1);
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(sessionId, $$"""{"text": "{{tooLong}}"}"""));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an oversized object key is rejected.</summary>
    [Fact]
    public async Task ObjectMemberKeyLongerThanTheLimitIsRejectedAsMalformedMessage()
    {
        // Distinct from a too-long string *value* (bounded_json.cpp's
        // FindLimitViolation checks member.key().size() separately from
        // member.value()'s own string-length check).
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string tooLongKey = new string('k', MaxStringLengthBytes + 1);
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(sessionId, $$"""{"{{tooLongKey}}": 1}"""));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an oversized array is rejected.</summary>
    [Fact]
    public async Task ArrayLongerThanTheLimitIsRejectedAsMalformedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string tooManyItems = string.Join(",", Enumerable.Repeat("1", MaxArrayItems + 1));
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(sessionId, $$"""{"items": [{{tooManyItems}}]}"""));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an object with too many members is rejected.</summary>
    [Fact]
    public async Task ObjectWithTooManyMembersIsRejectedAsMalformedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string tooManyMembers = string.Join(",", Enumerable.Range(0, MaxObjectMembers + 1).Select(i => $"\"k{i}\": 1"));
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(sessionId, $$"""{{{tooManyMembers}}}"""));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that string and array values at their limits are accepted.</summary>
    [Fact]
    public async Task StringAndArrayExactlyAtTheLimitAreAcceptedNormally()
    {
        // The off-by-one boundary itself: bounded_json.cpp's checks are
        // strictly "> limit", so a value exactly at the limit must succeed,
        // not just anything comfortably under it.
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string exactlyMaxString = new string('a', MaxStringLengthBytes);
        string exactlyMaxArray = string.Join(",", Enumerable.Repeat("1", MaxArrayItems));
        await connection.SendRawTextAsync(EnvelopeWithRawPayload(
            sessionId, $$"""{"text": "{{exactlyMaxString}}", "items": [{{exactlyMaxArray}}]}""", "message-at-limit"));

        // A well-formed ping payload is not schema-validated beyond being
        // an object (protocol/messages.hpp's ping decoder ignores unknown
        // fields), so this still succeeds as an ordinary ping.
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);
        Assert.Equal("message-at-limit", pong.CorrelationId);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an object at the member-count limit is accepted.</summary>
    [Fact]
    public async Task ObjectWithExactlyTheMaxMemberCountIsAcceptedNormally()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string exactlyMaxMembers = string.Join(",", Enumerable.Range(0, MaxObjectMembers).Select(i => $"\"k{i}\": 1"));
        await connection.SendRawTextAsync(
            EnvelopeWithRawPayload(sessionId, $$"""{{{exactlyMaxMembers}}}""", "message-at-limit-members"));

        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that a disallowed message type is rejected.</summary>
    [Fact]
    public async Task DisallowedMessageTypeIsRejectedAsMalformedMessage()
    {
        // A structurally well-formed envelope whose messageType simply
        // isn't one this connection can receive post-auth (message_dispatcher.cpp's
        // kAllowedMessageTypes) -- distinct from a JSON parse failure.
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-again"));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-hello-again", error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that valid non-object JSON is rejected as malformed.</summary>
    [Fact]
    public async Task ValidJsonThatIsNotAnEnvelopeObjectIsRejectedAsMalformedMessage()
    {
        // Valid JSON (an array), but DecodeEnvelope requires a top-level
        // object with the envelope's required fields -- distinct from
        // ParseBoundedJson itself failing.
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendRawTextAsync("[1, 2, 3]");

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Null(error.CorrelationId);
        Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that different violation types share one violation limit.</summary>
    [Fact]
    public async Task DifferentViolationTypesAccumulateTowardTheSameThreeInThirtySecondLimit()
    {
        // Proves ViolationTracker counts violations regardless of kind,
        // not per-type -- one malformed message, one replayed messageId,
        // then one more malformed message closes the connection on the
        // 3rd, even though no single type repeated three times.
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        await connection.SendRawTextAsync("not json {{{");
        Envelope first = await connection.ReceiveAsync();
        Assert.Equal("malformed_message", first.Payload["code"]!.GetValue<string>());

        var ping = new Envelope("ping", "message-mixed-dup", sessionId, null, new JsonObject());
        await connection.SendAsync(ping);
        await connection.ReceiveAsync();  // pong
        await connection.SendAsync(ping);  // same messageId again
        Envelope second = await connection.ReceiveAsync();
        Assert.Equal("replayed_message", second.Payload["code"]!.GetValue<string>());

        await connection.SendRawTextAsync("not json {{{");
        Envelope third = await connection.ReceiveAsync();
        Assert.Equal("malformed_message", third.Payload["code"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        await connection.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a duplicate message identifier is rejected as replayed.</summary>
    [Fact]
    public async Task DuplicateMessageIdIsRejectedAsReplayedMessage()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        var ping = new Envelope("ping", "message-dup-1", sessionId, null, new JsonObject());
        await connection.SendAsync(ping);
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await connection.SendAsync(ping);  // same messageId again
        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-dup-1", error.CorrelationId);
        Assert.Equal("replayed_message", error.Payload["code"]!.GetValue<string>());
        Assert.False(error.Payload["retryable"]!.GetValue<bool>());

        // One violation does not close the connection -- proven the same
        // way as the other scenario files, with a ping/pong right after.
        await connection.SendAsync(new Envelope("ping", "message-after-dup", sessionId, null, new JsonObject()));
        Envelope secondPong = await connection.ReceiveAsync();
        Assert.Equal("pong", secondPong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that the third violation closes the connection after its error.</summary>
    [Fact]
    public async Task ThirdProtocolViolationWithin30SecondsClosesTheConnectionAfterItsOwnErrorResponse()
    {
        (HarnessProcess harness, BridgeConnection connection, string _, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        for (int i = 1; i <= 2; i++)
        {
            await connection.SendRawTextAsync("not json {{{");
            Envelope error = await connection.ReceiveAsync();
            Assert.Equal("malformed_message", error.Payload["code"]!.GetValue<string>());
        }

        // The 3rd violation still gets its own error response (unlike an
        // oversized frame or the session cap) -- closeConnection is set
        // alongside it, not instead of it (application/message_dispatcher.cpp's Reject()).
        await connection.SendRawTextAsync("not json {{{");
        Envelope thirdError = await connection.ReceiveAsync();
        Assert.Equal("malformed_message", thirdError.Payload["code"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        await connection.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    // The 10,000-message session cap (ReplayGuard::RecordMessage,
    // application/replay_guard.cpp) is deliberately not re-proven live over
    // a socket here. It's already covered exactly, fast, and precisely at
    // the unit level by message_dispatcher_test.cpp's "closes with no
    // response once the session message cap is reached" (which pre-fills
    // ReplayGuard directly). Reaching the real boundary over a live
    // connection would also mean respecting the 100-messages-per-second
    // rate limit at the same time (confirmed directly: an early attempt at
    // this test, sending as fast as possible, tripped rate_limited around
    // message 100 instead of ever approaching the cap) -- a genuine
    // end-to-end proof would need over 100 real seconds of paced sends.
    // Making the cap itself independently configurable (mirroring
    // DOVAHLINK_HARNESS_TOKEN_TTL_SECONDS) would require threading a new
    // parameter through RunConnectionSession and BridgeWorkerPool, several
    // layers deeper into already-committed production code than the token
    // TTL override touched (harness main() only) -- a disproportionate
    // change for what the unit test already proves.

    /// <summary>Verifies that a burst exceeding 100 messages per second yields a rate-limit response.</summary>
    [Fact]
    public async Task AMessageBurstExceedingTheRateLimitIsDetectedAmongQueuedResponses()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        for (int i = 0; i <= 100; i++)
        {
            await connection.SendAsync(new Envelope("ping", $"message-rate-{i}", sessionId, null, new JsonObject()));
        }

        Envelope? rateLimited = null;
        int pongCount = 0;
        for (int i = 0; i <= 100; i++)
        {
            Envelope response = await connection.ReceiveAsync();
            if (response.Payload["code"]?.GetValue<string>() == "rate_limited")
            {
                rateLimited = response;
                break;
            }
            Assert.Equal("pong", response.MessageType);
            pongCount++;
        }

        Assert.NotNull(rateLimited);
        Assert.Equal(100, pongCount);
        Assert.Equal("error", rateLimited.MessageType);
        Assert.Equal("message-rate-100", rateLimited.CorrelationId);
        Assert.Equal("rate_limited", rateLimited.Payload["code"]!.GetValue<string>());
        Assert.True(rateLimited.Payload["retryable"]!.GetValue<bool>());

        // Deliberately not asserting the connection stays open with a
        // follow-up ping/pong here (unlike the other single-violation
        // scenarios in this file): rate_limited itself is also counted as
        // a protocol violation (message_dispatcher.cpp's design notes), so
        // a ping sent immediately after could land in the same rate window
        // and be rejected too, racking up a second violation and making
        // this assertion timing-dependent rather than deterministic.
        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }
}
