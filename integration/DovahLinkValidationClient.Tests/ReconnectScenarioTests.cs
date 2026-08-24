using System.Net.WebSockets;
using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises handshake timeout, stale-session, and abrupt-disconnect recovery.</summary>
/// <remarks>Successful-session reconnect and the full idle timeout remain outside Phase 1.</remarks>
public class ReconnectScenarioTests
{
    /// <summary>Verifies that a client that never sends hello is closed.</summary>
    [Fact]
    public async Task AClientThatNeverSendsHelloIsClosedWithinTheHandshakeTimeout()
    {
        // Real ~6s wait, matching the precedent already set in this project
        // for proving OS-level timeout enforcement over an actual socket
        // (bridge/transport/websocket_session_test.cpp).
        //
        // This proves the OS-level SO_RCVTIMEO path specifically (the
        // client sends nothing at all), not the application-level
        // ConnectionTimeoutTracker check added alongside it (a message that
        // arrives late but without any single read itself timing out --
        // the "trickle" case). That second path is only reachable by
        // sending real WebSocket ping frames while withholding hello, which
        // bridge/application/connection_session_test.cpp proves via Beast's
        // ws.ping() on the C++ side; .NET's ClientWebSocket exposes no
        // public API for sending a raw ping frame, so it isn't
        // independently reproducible here. Confirmed directly: an earlier
        // version of this test sent hello after the wait and asserted a
        // graceful close, but the OS-level timeout fires first (it starts
        // counting from Accept(), before this wait begins) and closes the
        // socket abruptly rather than via a WS close frame -- .NET surfaces
        // that as WebSocketException, not the InvalidOperationException a
        // graceful bridge-initiated close produces elsewhere in this suite.
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        await harness.WaitForReadyAsync();

        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => connection.ReceiveAsync(TimeSpan.FromSeconds(10)));
            await connection.CloseAsync();
        }

        // The accept loop must recover from this specific closure path too,
        // not only from an abrupt post-auth disconnect
        // (AbruptDisconnectMidRequest... below) -- a fresh connection
        // attempt must still be accepted and reach hello processing. The
        // first connection never sent hello at all, so the token was never
        // consumed: unlike AbruptDisconnectMidRequest's second connection,
        // this one succeeds normally.
        await using BridgeConnection second = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await second.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-after-timeout"));
        Envelope helloAck = await second.ReceiveAsync();
        Assert.Equal("hello_ack", helloAck.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, second);
    }

    /// <summary>Verifies that a foreign session identifier is rejected as stale.</summary>
    [Fact]
    public async Task ForeignSessionIdOnAnAuthenticatedConnectionIsRejectedAsStaleSession()
    {
        (HarnessProcess harness, BridgeConnection connection, string sessionId, Envelope _) =
            await BridgeScenario.ConnectAndAuthenticateAsync();
        using var disposeHarness = harness;
        await using var disposeConnection = connection;

        string foreignSessionId = sessionId + "-not-real";
        await connection.SendAsync(new Envelope("ping", "message-stale-1", foreignSessionId, null, new JsonObject()));

        Envelope error = await connection.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-stale-1", error.CorrelationId);
        Assert.Equal("stale_session", ErrorPayload.Decode(error.Payload).Code);
        // The error still carries this connection's real, valid sessionId
        // (message_dispatcher.cpp's Reject() echoes the connection's own
        // session, not the foreign one that was rejected).
        Assert.Equal(sessionId, error.SessionId);

        // A message carrying this connection's own real sessionId right
        // after still works normally -- the rejection is specific to the
        // mismatched ID, not a side effect that broke the connection.
        await connection.SendAsync(new Envelope("ping", "message-after-stale", sessionId, null, new JsonObject()));
        Envelope pong = await connection.ReceiveAsync();
        Assert.Equal("pong", pong.MessageType);

        await BridgeScenario.CloseAndQuitAsync(harness, connection);
    }

    /// <summary>Verifies that an abrupt disconnect leaves the bridge usable.</summary>
    [Fact]
    public async Task AbruptDisconnectMidRequestLeavesTheBridgeHealthyForTheNextConnectionAttempt()
    {
        using var harness = new HarnessProcess(BridgeScenario.ValidHexToken);
        await harness.WaitForReadyAsync();

        await using (BridgeConnection connection = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri))
        {
            await connection.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-abort"));
            Envelope helloAck = await connection.ReceiveAsync();
            string sessionId = helloAck.SessionId!;
            await connection.ReceiveAsync();  // the bridge's own capabilities message

            // Sent but deliberately never read: simulates a real network
            // drop or crash while a request is outstanding, not a graceful
            // close.
            await connection.SendAsync(new Envelope("snapshot_request", "message-snap-abort", sessionId, null,
                new SnapshotRequestPayload("character").Encode()));
            connection.Abort();
        }

        // The bridge must notice the drop and release both the connection
        // slot and the ended session promptly, not hang until some timeout
        // -- a fresh connection attempt must reach hello processing
        // quickly (both releases happen together, synchronously, in
        // connection_session.cpp's cleanup path; this test cannot and does
        // not need to distinguish which one specifically unblocks the
        // accept loop). Its token is already spent (this test's own
        // connection just consumed it) -- unauthenticated here proves the
        // bridge is alive and correctly enforcing that invariant, not that
        // a new session was granted (which Phase 1 cannot do again without
        // restarting the bridge -- see this file's class-level comment).
        await using BridgeConnection second = await BridgeConnection.ConnectWithRetryAsync(BridgeScenario.BridgeUri);
        await second.SendAsync(BridgeScenario.HelloEnvelope(BridgeScenario.ValidHexToken, "message-hello-after-abort"));
        Envelope error = await second.ReceiveAsync();
        Assert.Equal("error", error.MessageType);
        Assert.Equal("message-hello-after-abort", error.CorrelationId);
        Assert.Equal("unauthenticated", ErrorPayload.Decode(error.Payload).Code);

        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ReceiveAsync());
        await second.CloseAsync();

        await harness.WriteLineAsync("quit");
        Assert.True(await harness.WaitForExitAsync(TimeSpan.FromSeconds(5)));
    }
}
