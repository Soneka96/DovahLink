using System.Net.WebSockets;
using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises authentication, token lifetime, and connection-slot scenarios.</summary>
public class AuthScenarioTests
{
    /// <summary>Verifies that a rejected token does not consume the real token.</summary>
    [Fact]
    public async Task InvalidTokenIsRejectedAndTheRealTokenStaysAvailable()
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        string wrongButValidHexToken = new string('f', 64);
        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(wrongButValidHexToken, "message-hello-wrong"));

            Envelope error = await connection.ReceiveAsync();
            Assert.Equal("error", error.MessageType);
            Assert.Equal("unauthenticated", error.Payload["code"]!.GetValue<string>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
            await connection.CloseAsync();
        }

        // The failed attempt must not have consumed the real token: a fresh
        // connection with the correct one still succeeds.
        await using BridgeConnection secondConnection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await secondConnection.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-correct"));
        Envelope helloAck = await secondConnection.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, secondConnection);
    }

    /// <summary>Verifies that a one-time token cannot authenticate twice.</summary>
    [Fact]
    public async Task ReusedTokenIsRejectedOnASecondConnection()
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        // First connection consumes the one-time token and disconnects
        // cleanly, freeing the connection slot for the next attempt.
        await using (BridgeConnection first = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await first.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-first"));
            Envelope helloAck = await first.ReceiveAsync();
            Assert.Equal("hello_ack", helloAck.MessageType);
            await first.CloseAsync();
        }

        // Second connection presents the same, now-consumed token.
        await using BridgeConnection second = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await second.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-reused"));

        Envelope error = await second.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("unauthenticated", error.Payload["code"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ReceiveAsync());
        await second.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that an expired token is rejected by the bridge.</summary>
    [Fact]
    public async Task TokenIsRejectedAfterItsShortenedTtlElapses()
    {
        // The harness-only TTL override (bridge/harness/dovahlink_bridge_harness.cpp)
        // exists specifically so this scenario can prove real expiry
        // end to end without sleeping out TokenStore's normal 5-minute
        // default; TryConsume's own expiry arithmetic is already covered
        // at the unit level by security/token_store_test.cpp.
        using var harness = new HarnessProcess(
            BridgeScenario.ValidHexToken,
            new Dictionary<string, string> { ["DOVAHLINK_HARNESS_TOKEN_TTL_SECONDS"] = "1" });
        Assert.Equal("READY", await harness.ReadLineAsync());

        await Task.Delay(TimeSpan.FromSeconds(2));

        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-expired"));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("unauthenticated", error.Payload["code"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        await connection.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a structurally invalid token is unauthenticated.</summary>
    [Fact]
    public async Task StructurallyInvalidTokenIsRejectedAsUnauthenticated()
    {
        // Distinct from "valid hex, wrong value" (every other test here):
        // handshake_handler.cpp skips TryConsume entirely when hex decoding
        // itself fails, a different branch producing the same wire error.
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        string nonHexToken = "zz" + new string('a', 62);
        await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await connection.SendAsync(BridgeScenario.HelloEnvelope(nonHexToken, "message-hello-nonhex"));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("unauthenticated", error.Payload["code"]!.GetValue<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReceiveAsync());
        await connection.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that the sixth failed token attempt is rate limited.</summary>
    [Fact]
    public async Task SixthFailedTokenAttemptWithinTheWindowIsRateLimited()
    {
        // FailedTokenThrottle is one instance shared across every connection
        // attempt for the harness's whole lifetime (security/throttle.hpp),
        // not per-connection -- so five separate failed connections, one
        // after another, are needed to reach the limit.
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        string wrongButValidHexToken = new string('e', 64);
        await BridgeScenario.RecordFailedTokenAttemptsAsync(5, wrongButValidHexToken);

        await using BridgeConnection sixth = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await sixth.SendAsync(BridgeScenario.HelloEnvelope(wrongButValidHexToken, "message-hello-fail-6"));

        Envelope rateLimited = await sixth.ReceiveAsync();
        Assert.Equal("error", rateLimited.MessageType);
        Assert.Equal("rate_limited", rateLimited.Payload["code"]!.GetValue<string>());
        Assert.True(rateLimited.Payload["retryable"]!.GetValue<bool>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sixth.ReceiveAsync());
        await sixth.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a correct token succeeds after failed attempts.</summary>
    [Fact]
    public async Task CorrectTokenStillSucceedsImmediatelyAfterFiveFailedAttempts()
    {
        // Documents actual behavior, not an asserted requirement:
        // handshake_handler.cpp only checks/records the failed-attempt
        // throttle inside the "token did not match" branch, so a correct
        // token bypasses it entirely regardless of how many recent
        // failures preceded it. Worth a second look against
        // ai/context/protocol/security.md's literal "further attempts are
        // rejected until the window expires" -- this test makes that gap
        // visible rather than asserting it is the intended behavior.
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        string wrongButValidHexToken = new string('d', 64);
        await BridgeScenario.RecordFailedTokenAttemptsAsync(5, wrongButValidHexToken);

        await using BridgeConnection sixth = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await sixth.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-correct-after-failures"));
        Envelope helloAck = await sixth.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, sixth);
    }

    /// <summary>Verifies that the single connection slot rejects a second client.</summary>
    [Fact]
    public async Task SecondClientCannotConnectWhileFirstHoldsTheOneConnectionSlot()
    {
        (HarnessProcess harness, BridgeConnection first, string _, Envelope _) = await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeFirst = first;

        // ConnectionSlot enforces "one connected client"
        // (ai/context/protocol/security.md) at accept time, before any
        // WebSocket handshake -- rejected there, not with a protocol-level
        // error. Using the IPv6 loopback listener for the second attempt
        // (the first connected over IPv4) so it hits its own accept-loop
        // thread and gets a fast rejection, rather than sitting unaccepted
        // behind the first connection's busy IPv4 thread until it
        // disconnects.
        var secondUri = new Uri("ws://[::1]:58231/");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using BridgeConnection second = new();
            await second.ConnectAsync(secondUri);
        });

        await BridgeScenario.CloseAndQuitAsync(harness, first);
    }

    /// <summary>Verifies that concurrent connection attempts allow exactly one winner.</summary>
    [Fact]
    public async Task ConcurrentConnectionAttemptsRaceForTheOneSlotAndExactlyOneWins()
    {
        // The sequential test above proves a second attempt is rejected
        // once the first already holds the slot; this proves the actual
        // race itself -- ai/context/integration/testing.md's "concurrent
        // proof-client attempts". IPv4 and IPv6 each have their own
        // accept-loop thread (BridgeWorkerPool), so launching one attempt
        // on each lets both genuinely race ConnectionSlot::TryAcquire's
        // atomic compare-and-swap concurrently, rather than one attempt
        // sitting queued behind the other on the same thread.
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        Assert.Equal("READY", await harness.ReadLineAsync());

        Task<bool> attemptV4 = TryConnectAndHelloAsync(new Uri("ws://127.0.0.1:58231/"));
        Task<bool> attemptV6 = TryConnectAndHelloAsync(new Uri("ws://[::1]:58231/"));
        bool[] results = await Task.WhenAll(attemptV4, attemptV6);

        Assert.Equal(1, results.Count(succeeded => succeeded));

        // The winning connection is left open (this test doesn't track
        // which attempt it was to close it cleanly); Dispose kills the
        // process, already a documented, safe fallback (HarnessProcess.Dispose).
    }

    /// <summary>Attempts one connection and hello exchange, returning whether it succeeds.</summary>
    private static async Task<bool> TryConnectAndHelloAsync(Uri uri)
    {
        try
        {
            await using BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(uri, maxAttempts: 1);
            await connection.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken));
            Envelope response = await connection.ReceiveAsync();
            return response.MessageType == "hello_ack";
        }
        catch (Exception)
        {
            return false;
        }
    }
}
