using System.Buffers.Binary;
using System.Security.Cryptography;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterIpcSession"/>.</summary>
public class AdapterIpcSessionTests
{
    // ---- Handshake ----

    /// <summary>Verifies that a valid Hello is accepted without publishing availability until commitment.</summary>
    [Fact]
    public void Handshake_ValidProof_Accepts()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        var hello = new IpcHelloMessage(7, instanceId, verifier.ExpectedToken);

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.True(result.Accepted);
        Assert.True(result.AckMessage.Accepted);
        Assert.Equal(IpcHelloRejectReason.None, result.AckMessage.RejectReason);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.Null(session.ConnectionGeneration);

        session.CommitHandshake();

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.NotNull(session.ConnectionGeneration);
    }

    /// <summary>Verifies that committing an accepted handshake twice publishes only one connection generation.</summary>
    [Fact]
    public void CommitHandshake_CalledTwice_PublishesOnce()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);

        session.Handshake(new IpcHelloMessage(7, AdapterInstanceId.NewId(), verifier.ExpectedToken));
        session.CommitHandshake();
        session.CommitHandshake();

        Assert.Equal(1, tracker.CurrentConnectionGeneration);
        Assert.Equal(AdapterAvailability.Available, tracker.Current);
    }

    /// <summary>
    /// Verifies that an accepted handshake's HostProof matches an independently computed HMAC-SHA256
    /// over the exact challenge/correlationId/adapterInstanceId/ownerLifetimeId field layout, and
    /// that a rejected handshake's HostProof stays all-zero.
    /// </summary>
    [Fact]
    public void Handshake_ValidProof_ComputesExpectedHostProof()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);
        var session = new AdapterIpcSession(tracker, verifier, ownerLifetimeId);
        byte[] challenge = Enumerable.Range(1, Constants.IpcChallengeBytes).Select(index => (byte)index).ToArray();
        var hello = new IpcHelloMessage(
            7, AdapterInstanceId.NewId(), verifier.ExpectedToken, challenge, ownerLifetimeId.ToBytes());

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.True(result.Accepted);
        Assert.Equal(IndependentlyComputedHostProof(hello, verifier.ExpectedToken), result.AckMessage.HostProof);
    }

    /// <summary>Verifies that a rejected handshake's HostProof is all-zero -- the host never computes a real proof for a peer it refuses.</summary>
    [Fact]
    public void Handshake_WrongProof_HostProofStaysAllZero()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        var hello = new IpcHelloMessage(7, AdapterInstanceId.NewId(), [1, 2, 3]);

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.Equal(new byte[Constants.IpcHostProofBytes], result.AckMessage.HostProof);
        session.CommitHandshake();
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.Null(session.ConnectionGeneration);
    }

    /// <summary>Verifies that a Hello with a matching proof but a mismatched owner-lifetime-id is rejected, even though the proof itself is correct.</summary>
    [Fact]
    public void Handshake_MatchingProofWrongLifetimeId_RejectsWithLifetimeMismatch()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier, new OwnerLifetimeId(1, 2));
        var hello = new IpcHelloMessage(
            7, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: new OwnerLifetimeId(3, 4).ToBytes());

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.False(result.Accepted);
        Assert.False(result.AckMessage.Accepted);
        Assert.Equal(IpcHelloRejectReason.LifetimeMismatch, result.AckMessage.RejectReason);
        Assert.Null(session.ConnectionGeneration);
        Assert.Equal(new byte[Constants.IpcHostProofBytes], result.AckMessage.HostProof);
    }

    /// <summary>Verifies that a Hello with a matching lifetime id but a mismatched proof is still rejected for the proof reason, not the lifetime.</summary>
    [Fact]
    public void Handshake_MatchingLifetimeIdWrongProof_RejectsWithInvalidProof()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);
        var session = new AdapterIpcSession(tracker, verifier, ownerLifetimeId);
        var hello = new IpcHelloMessage(7, AdapterInstanceId.NewId(), [1, 2, 3], ownerLifetimeId: ownerLifetimeId.ToBytes());

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.False(result.Accepted);
        Assert.Equal(IpcHelloRejectReason.InvalidProof, result.AckMessage.RejectReason);
    }

    /// <summary>Independently recomputes the expected HostProof, mirroring the production byte layout without calling its private helper.</summary>
    private static byte[] IndependentlyComputedHostProof(IpcHelloMessage hello, byte[] peerProofToken)
    {
        var message = new byte[Constants.IpcHostProofMessageBytes];
        hello.Challenge.CopyTo(message, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(Constants.IpcChallengeBytes, 8), hello.CorrelationId);
        hello.AdapterInstanceId.Value.TryWriteBytes(message.AsSpan(Constants.IpcChallengeBytes + 8, 16), bigEndian: true, out _);
        hello.OwnerLifetimeId.CopyTo(message, Constants.IpcChallengeBytes + 8 + 16);

        using var hmac = new HMACSHA256(peerProofToken);
        return hmac.ComputeHash(message);
    }

    /// <summary>Verifies that a Hello with a mismatched proof is rejected without connecting the tracker.</summary>
    [Fact]
    public void Handshake_WrongProof_RejectsWithInvalidProof()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        var hello = new IpcHelloMessage(7, AdapterInstanceId.NewId(), [1, 2, 3]);

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.False(result.Accepted);
        Assert.False(result.AckMessage.Accepted);
        Assert.Equal(IpcHelloRejectReason.InvalidProof, result.AckMessage.RejectReason);
        Assert.Null(session.ConnectionGeneration);
    }

    /// <summary>Verifies that a Hello carrying an empty adapter instance id is rejected as malformed.</summary>
    [Fact]
    public void Handshake_EmptyInstanceId_RejectsWithMalformed()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        var hello = new IpcHelloMessage(7, new AdapterInstanceId(Guid.Empty), verifier.ExpectedToken);

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.False(result.Accepted);
        Assert.Equal(IpcHelloRejectReason.Malformed, result.AckMessage.RejectReason);
        Assert.Null(session.ConnectionGeneration);
        Assert.Equal(new byte[Constants.IpcHostProofBytes], result.AckMessage.HostProof);
    }

    /// <summary>Verifies that the acknowledgement always carries the Hello's own correlation id, accepted or not.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Handshake_AckMessage_PreservesHelloCorrelationId(bool validProof)
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        byte[] token = validProof ? verifier.ExpectedToken : [9, 9, 9];
        var hello = new IpcHelloMessage(42, AdapterInstanceId.NewId(), token);

        AdapterHandshakeResult result = session.Handshake(hello);

        Assert.Equal(42UL, result.AckMessage.CorrelationId);
    }

    // ---- Resynchronization ----

    /// <summary>Verifies that an accepted resynchronization result matching the pending correlation id resynchronizes the tracker.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_MatchingCorrelationAccepted_NotifiesResynchronized()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true));

        Assert.False(tracker.NeedsResynchronization);
        Assert.Empty(outcome.MessagesToSend);
        Assert.False(outcome.ShouldClose);
    }

    /// <summary>Verifies that a resynchronization result with a mismatched correlation id is ignored safely.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_MismatchedCorrelation_IsIgnored()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId + 1, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that a declined resynchronization result does not clear the pending requirement.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_NotAccepted_DoesNotResynchronize()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();

        session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: false));

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that an accepted result is ignored when the tracker has since moved to a different connection generation.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_StaleGeneration_DoesNotResynchronize()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();
        tracker.NotifyConnected(AdapterInstanceId.NewId());

        session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a repeated result for an already-consumed correlation id is ignored rather than re-applied.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_RepeatedForSameCorrelation_IsIgnored()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();
        session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true));
        tracker.NeedsResynchronization = true;

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that each resynchronization request receives a fresh, nonzero correlation id.</summary>
    [Fact]
    public void PrepareResynchronizeRequest_IssuesDistinctNonzeroCorrelationIds()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        IpcResynchronizeRequestMessage first = session.PrepareResynchronizeRequest();
        IpcResynchronizeRequestMessage second = session.PrepareResynchronizeRequest();

        Assert.NotEqual(0UL, first.CorrelationId);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
    }

    /// <summary>Verifies that issuing a second resynchronization request supersedes the first: a late result for the first is ignored.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_ForSupersededEarlierRequest_IsIgnored()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage first = session.PrepareResynchronizeRequest();
        session.PrepareResynchronizeRequest();

        session.HandleFrame(new IpcResynchronizeResultMessage(first.CorrelationId, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a stray resynchronization result with correlation id zero and no pending request is ignored safely.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_ZeroCorrelationWithNoPendingRequest_IsIgnored()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(0, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that a repeated result for an already-consumed, declined correlation id is also ignored.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_RepeatedAfterDeclined_IsIgnored()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();
        IpcResynchronizeRequestMessage request = session.PrepareResynchronizeRequest();
        session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: false));

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true));

        Assert.True(tracker.NeedsResynchronization);
        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that a resynchronization result arriving before any handshake is ignored safely.</summary>
    [Fact]
    public void HandleFrame_ResynchronizeResult_BeforeHandshake_IsIgnoredSafely()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var session = new AdapterIpcSession(tracker, new AdapterPeerProofVerifier());

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcResynchronizeResultMessage(1, Accepted: true));

        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that preparing an event-listening intent with a zero key returns null even after handshake.</summary>
    [Fact]
    public void PrepareListenEvent_ZeroKey_ReturnsNull()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        Assert.Null(session.PrepareListenEvent(0));
    }

    /// <summary>Verifies that preparing a sample-read intent with a zero token returns null even after handshake.</summary>
    [Fact]
    public void PrepareReadSample_ZeroToken_ReturnsNull()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        Assert.Null(session.PrepareReadSample(0));
    }

    // ---- Other post-handshake frames ----

    /// <summary>Verifies that a Close message closes without any reply.</summary>
    [Fact]
    public void HandleFrame_Close_ReturnsCloseOutcome()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcCloseMessage(0, IpcCloseReason.Normal));

        Assert.Equal(AdapterIpcOutcome.Close, outcome);
    }

    /// <summary>Verifies that a Reject message is a harmless no-op that keeps the connection open.</summary>
    [Fact]
    public void HandleFrame_Reject_ReturnsNoneOutcome()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcRejectMessage(3, IpcRejectReason.MalformedPayload));

        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that a Cancel message is a harmless no-op that keeps the connection open.</summary>
    [Fact]
    public void HandleFrame_Cancel_ReturnsNoneOutcome()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        AdapterIpcOutcome outcome = session.HandleFrame(new IpcCancelMessage(3));

        Assert.Equal(AdapterIpcOutcome.None, outcome);
    }

    /// <summary>Verifies that a message kind the host never expects to receive is rejected and closes the connection.</summary>
    [Theory]
    [InlineData(typeof(IpcListenEventMessage))]
    [InlineData(typeof(IpcReadSampleMessage))]
    [InlineData(typeof(IpcHelloAckMessage))]
    public void HandleFrame_UnexpectedMessageKind_RejectsAndCloses(Type messageType)
    {
        (AdapterIpcSession session, _) = HandshakenSession();
        IpcMessage message = messageType == typeof(IpcListenEventMessage)
            ? new IpcListenEventMessage(5, 1)
            : messageType == typeof(IpcReadSampleMessage)
                ? new IpcReadSampleMessage(5, 1)
                : new IpcHelloAckMessage(5, true, IpcHelloRejectReason.None);

        AdapterIpcOutcome outcome = session.HandleFrame(message);

        Assert.True(outcome.ShouldClose);
        var reject = Assert.IsType<IpcRejectMessage>(Assert.Single(outcome.MessagesToSend));
        Assert.Equal(5UL, reject.CorrelationId);
        Assert.Equal(IpcRejectReason.UnknownMessageKind, reject.Reason);
    }

    // ---- Decode failures ----

    /// <summary>Verifies that a decode failure always closes with an error close message and no echoed reason.</summary>
    [Fact]
    public void HandleDecodeFailure_ReturnsErrorCloseAndCloses()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        AdapterIpcOutcome outcome = session.HandleDecodeFailure();

        Assert.True(outcome.ShouldClose);
        var close = Assert.IsType<IpcCloseMessage>(Assert.Single(outcome.MessagesToSend));
        Assert.Equal(IpcCloseReason.Error, close.Reason);
    }

    // ---- Capture-intent preparation ----

    /// <summary>Verifies that preparing a capture intent before a successful handshake returns null.</summary>
    [Fact]
    public void PrepareListenEvent_BeforeHandshake_ReturnsNull()
    {
        var session = new AdapterIpcSession(new FakeAdapterAvailabilityTracker(), new AdapterPeerProofVerifier());

        Assert.Null(session.PrepareListenEvent(1));
    }

    /// <summary>Verifies that capture intents remain unavailable after validation but before handshake commitment.</summary>
    [Fact]
    public void PrepareCaptureIntent_BeforeCommit_ReturnsNull()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        session.Handshake(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken));

        Assert.Null(session.PrepareListenEvent(1));
        Assert.Null(session.PrepareReadSample(1));
        Assert.Null(session.PrepareCancel(1));
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that preparing an event-listening intent after handshake returns a message with a nonzero correlation id and the requested key.</summary>
    [Fact]
    public void PrepareListenEvent_AfterHandshake_ReturnsMessage()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        IpcListenEventMessage? message = session.PrepareListenEvent(42);

        Assert.NotNull(message);
        Assert.NotEqual(0UL, message.CorrelationId);
        Assert.Equal(42u, message.EventKey);
    }

    /// <summary>Verifies that preparing a sample-read intent before a successful handshake returns null.</summary>
    [Fact]
    public void PrepareReadSample_BeforeHandshake_ReturnsNull()
    {
        var session = new AdapterIpcSession(new FakeAdapterAvailabilityTracker(), new AdapterPeerProofVerifier());

        Assert.Null(session.PrepareReadSample(1));
    }

    /// <summary>Verifies that preparing a sample-read intent after handshake returns a message with a nonzero correlation id and the requested token.</summary>
    [Fact]
    public void PrepareReadSample_AfterHandshake_ReturnsMessage()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        IpcReadSampleMessage? message = session.PrepareReadSample(99);

        Assert.NotNull(message);
        Assert.NotEqual(0UL, message.CorrelationId);
        Assert.Equal(99u, message.SampleToken);
    }

    /// <summary>Verifies that successive capture-intent preparations issue distinct correlation ids.</summary>
    [Fact]
    public void PrepareCaptureIntents_IssueDistinctCorrelationIds()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        IpcListenEventMessage? first = session.PrepareListenEvent(1);
        IpcReadSampleMessage? second = session.PrepareReadSample(1);

        Assert.NotEqual(first!.CorrelationId, second!.CorrelationId);
    }

    /// <summary>Verifies that preparing a cancellation before a successful handshake returns null.</summary>
    [Fact]
    public void PrepareCancel_BeforeHandshake_ReturnsNull()
    {
        var session = new AdapterIpcSession(new FakeAdapterAvailabilityTracker(), new AdapterPeerProofVerifier());

        Assert.Null(session.PrepareCancel(1));
    }

    /// <summary>Verifies that preparing a cancellation for correlation id zero returns null even after handshake.</summary>
    [Fact]
    public void PrepareCancel_ZeroCorrelationId_ReturnsNull()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        Assert.Null(session.PrepareCancel(0));
    }

    /// <summary>Verifies that preparing a cancellation for a nonzero correlation id after handshake returns the cancellation.</summary>
    [Fact]
    public void PrepareCancel_AfterHandshake_ReturnsMessage()
    {
        (AdapterIpcSession session, _) = HandshakenSession();

        IpcCancelMessage? cancel = session.PrepareCancel(7);

        Assert.NotNull(cancel);
        Assert.Equal(7UL, cancel.CorrelationId);
    }

    // ---- Disconnect ----

    /// <summary>Verifies that disconnecting after a successful handshake notifies the tracker.</summary>
    [Fact]
    public void HandleDisconnected_AfterHandshake_NotifiesTracker()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();

        session.HandleDisconnected();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that disconnecting twice is a harmless, idempotent no-op.</summary>
    [Fact]
    public void HandleDisconnected_CalledTwice_DoesNotThrow()
    {
        (AdapterIpcSession session, FakeAdapterAvailabilityTracker tracker) = HandshakenSession();

        session.HandleDisconnected();
        session.HandleDisconnected();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that disconnecting before a successful handshake does not throw or notify the tracker.</summary>
    [Fact]
    public void HandleDisconnected_BeforeHandshake_DoesNotThrow()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var session = new AdapterIpcSession(tracker, new AdapterPeerProofVerifier());

        session.HandleDisconnected();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Creates a session that has already completed and committed a successful handshake against a fresh tracker.</summary>
    private static (AdapterIpcSession Session, FakeAdapterAvailabilityTracker Tracker) HandshakenSession()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var verifier = new AdapterPeerProofVerifier();
        var session = new AdapterIpcSession(tracker, verifier);
        session.Handshake(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken));
        session.CommitHandshake();
        return (session, tracker);
    }
}
