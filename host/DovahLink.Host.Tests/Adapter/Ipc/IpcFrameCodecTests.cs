using System.Buffers.Binary;
using DovahLink.Host;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="IpcFrameCodec"/>.</summary>
public class IpcFrameCodecTests
{
    /// <summary>Encodes a message, reads its declared length, and decodes the result, mirroring real usage.</summary>
    private static (IpcDecodeResult Result, int FrameLength) EncodeThenDecode(IIpcFrameCodec codec, IpcMessage message)
    {
        byte[] frameBytes = codec.Encode(message);
        Assert.True(codec.TryReadFrameLength(frameBytes.AsSpan(0, 4), out int frameLength));
        Assert.Equal(frameBytes.Length - 4, frameLength);
        return (codec.Decode(frameBytes.AsSpan(4, frameLength)), frameLength);
    }

    // ---- Round trips ----

    /// <summary>Verifies that a Hello with a non-empty peer-proof token round-trips exactly.</summary>
    [Fact]
    public void RoundTrip_Hello_WithToken()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloMessage(correlationId: 7, AdapterInstanceId.NewId(), peerProofToken: [1, 2, 3, 4]);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Null(result.FailureReason);
        var decoded = Assert.IsType<IpcHelloMessage>(result.Message);
        Assert.Equal(original.CorrelationId, decoded.CorrelationId);
        Assert.Equal(original.AdapterInstanceId, decoded.AdapterInstanceId);
        Assert.True(original.PeerProofToken.SequenceEqual(decoded.PeerProofToken));
    }

    /// <summary>Verifies that constructing a Hello takes ownership of a copy rather than the caller's mutable array.</summary>
    [Fact]
    public void Hello_CopiesPeerProofTokenOnConstruction()
    {
        byte[] sourceToken = [1, 2, 3];
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), sourceToken);
        sourceToken[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, message.PeerProofToken);
    }

    /// <summary>Verifies that mutating a previously read PeerProofToken cannot change the message's stored value.</summary>
    [Fact]
    public void Hello_PeerProofToken_MutatingReturnedArrayDoesNotAffectMessage()
    {
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), [1, 2, 3]);

        byte[] returnedToken = message.PeerProofToken;
        returnedToken[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, message.PeerProofToken);
    }

    /// <summary>Verifies that each read of PeerProofToken returns an independent array, not a shared cached copy.</summary>
    [Fact]
    public void Hello_PeerProofToken_EachReadReturnsIndependentArray()
    {
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), [1, 2, 3]);

        byte[] firstRead = message.PeerProofToken;
        byte[] secondRead = message.PeerProofToken;
        firstRead[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, secondRead);
    }

    /// <summary>Verifies that decoding a Hello copies identity and token bytes out of the source frame.</summary>
    [Fact]
    public void Decode_Hello_OwnsDecodedBytes()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloMessage(1, AdapterInstanceId.NewId(), [1, 2, 3]);
        byte[] frame = codec.Encode(original);

        IpcDecodeResult result = codec.Decode(frame.AsSpan(4));
        var decoded = Assert.IsType<IpcHelloMessage>(result.Message);
        frame[13] = 99;
        frame[30] = 99;

        Assert.Equal(original.AdapterInstanceId, decoded.AdapterInstanceId);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.PeerProofToken);
    }

    /// <summary>Verifies that mutating a previously read Challenge cannot change the message's stored value.</summary>
    [Fact]
    public void Hello_Challenge_MutatingReturnedArrayDoesNotAffectMessage()
    {
        byte[] challenge = new byte[Constants.IpcChallengeBytes];
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), [], challenge);

        byte[] returned = message.Challenge;
        returned[0] = 99;

        Assert.Equal(new byte[Constants.IpcChallengeBytes], message.Challenge);
    }

    /// <summary>Verifies that mutating a previously read OwnerLifetimeId cannot change the message's stored value.</summary>
    [Fact]
    public void Hello_OwnerLifetimeId_MutatingReturnedArrayDoesNotAffectMessage()
    {
        byte[] ownerLifetimeId = new byte[Constants.IpcOwnerLifetimeIdBytes];
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), [], ownerLifetimeId: ownerLifetimeId);

        byte[] returned = message.OwnerLifetimeId;
        returned[0] = 99;

        Assert.Equal(new byte[Constants.IpcOwnerLifetimeIdBytes], message.OwnerLifetimeId);
    }

    /// <summary>Verifies that a Hello round-trips its challenge and owner-lifetime-id exactly.</summary>
    [Fact]
    public void RoundTrip_Hello_ChallengeAndOwnerLifetimeId()
    {
        var codec = new IpcFrameCodec();
        byte[] challenge = Enumerable.Range(1, Constants.IpcChallengeBytes).Select(index => (byte)index).ToArray();
        byte[] ownerLifetimeId = Enumerable.Range(200, Constants.IpcOwnerLifetimeIdBytes).Select(index => (byte)index).ToArray();
        var original = new IpcHelloMessage(7, AdapterInstanceId.NewId(), [1, 2], challenge, ownerLifetimeId);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloMessage>(result.Message);
        Assert.Equal(challenge, decoded.Challenge);
        Assert.Equal(ownerLifetimeId, decoded.OwnerLifetimeId);
    }

    /// <summary>Verifies that constructing a Hello rejects a challenge or owner-lifetime-id of the wrong length.</summary>
    [Fact]
    public void Construct_Hello_WrongLengthChallengeOrOwnerLifetimeId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new IpcHelloMessage(1, AdapterInstanceId.NewId(), [], challenge: new byte[Constants.IpcChallengeBytes - 1]));
        Assert.Throws<ArgumentException>(() =>
            new IpcHelloMessage(1, AdapterInstanceId.NewId(), [], ownerLifetimeId: new byte[Constants.IpcOwnerLifetimeIdBytes + 1]));
    }

    /// <summary>Verifies that a Hello payload missing the challenge and owner-lifetime-id tail fails closed.</summary>
    [Fact]
    public void Decode_Hello_MissingChallengeAndOwnerLifetimeIdTail_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        // The pre-D2 payload shape: 17 identity/length bytes plus a 2-byte token, with none of the
        // new fixed-size fields appended.
        byte[] payload = new byte[19];
        payload[16] = 2;
        byte[] frame = BuildFrame(IpcMessageKind.Hello, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Hello with an empty peer-proof token round-trips.</summary>
    [Fact]
    public void RoundTrip_Hello_EmptyToken()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloMessage(1, AdapterInstanceId.NewId(), peerProofToken: []);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloMessage>(result.Message);
        Assert.Empty(decoded.PeerProofToken);
    }

    /// <summary>Verifies that a Hello with the maximum allowed peer-proof token length round-trips.</summary>
    [Fact]
    public void RoundTrip_Hello_MaxLengthToken()
    {
        var codec = new IpcFrameCodec();
        byte[] token = Enumerable.Range(0, Constants.MaxIpcPeerProofTokenBytes).Select(index => (byte)index).ToArray();
        var original = new IpcHelloMessage(1, AdapterInstanceId.NewId(), token);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloMessage>(result.Message);
        Assert.True(token.SequenceEqual(decoded.PeerProofToken));
    }

    /// <summary>Verifies that an accepted HelloAck round-trips.</summary>
    [Fact]
    public void RoundTrip_HelloAck_Accepted()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloAckMessage>(result.Message);
        Assert.Equal(original.CorrelationId, decoded.CorrelationId);
        Assert.Equal(original.Accepted, decoded.Accepted);
        Assert.Equal(original.RejectReason, decoded.RejectReason);
        Assert.Equal(original.HostProof, decoded.HostProof);
    }

    /// <summary>Verifies that mutating a previously read HostProof cannot change the message's stored value.</summary>
    [Fact]
    public void HelloAck_HostProof_MutatingReturnedArrayDoesNotAffectMessage()
    {
        byte[] hostProof = new byte[Constants.IpcHostProofBytes];
        var message = new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None, hostProof);

        byte[] returned = message.HostProof;
        returned[0] = 99;

        Assert.Equal(new byte[Constants.IpcHostProofBytes], message.HostProof);
    }

    /// <summary>Verifies that an accepted HelloAck round-trips its host proof exactly.</summary>
    [Fact]
    public void RoundTrip_HelloAck_HostProof()
    {
        var codec = new IpcFrameCodec();
        byte[] hostProof = Enumerable.Range(1, Constants.IpcHostProofBytes).Select(index => (byte)index).ToArray();
        var original = new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None, hostProof);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloAckMessage>(result.Message);
        Assert.Equal(hostProof, decoded.HostProof);
    }

    /// <summary>Verifies that constructing a HelloAck rejects a host proof of the wrong length.</summary>
    [Fact]
    public void Construct_HelloAck_WrongLengthHostProof_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None, new byte[Constants.IpcHostProofBytes - 1]));
    }

    /// <summary>Verifies that a HelloAck payload missing the host-proof tail fails closed.</summary>
    [Fact]
    public void Decode_HelloAck_MissingHostProofTail_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        // The pre-D2 2-byte payload shape, with no hostProof appended.
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1, [1, 0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that encoding an accepted HelloAck with a rejection reason fails closed.</summary>
    [Fact]
    public void Encode_HelloAck_AcceptedWithRejectReason_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcHelloAckMessage(1, true, IpcHelloRejectReason.InvalidProof);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that encoding a rejected HelloAck without a rejection reason fails closed.</summary>
    [Fact]
    public void Encode_HelloAck_RejectedWithoutRejectReason_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcHelloAckMessage(1, false, IpcHelloRejectReason.None);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that a rejected HelloAck round-trips for every non-<see cref="IpcHelloRejectReason.None"/> reason.</summary>
    [Theory]
    [InlineData(IpcHelloRejectReason.InvalidProof)]
    [InlineData(IpcHelloRejectReason.Malformed)]
    [InlineData(IpcHelloRejectReason.LifetimeMismatch)]
    public void RoundTrip_HelloAck_Rejected(IpcHelloRejectReason reason)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloAckMessage(1, false, reason);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        var decoded = Assert.IsType<IpcHelloAckMessage>(result.Message);
        Assert.Equal(original.CorrelationId, decoded.CorrelationId);
        Assert.Equal(original.Accepted, decoded.Accepted);
        Assert.Equal(original.RejectReason, decoded.RejectReason);
        Assert.Equal(original.HostProof, decoded.HostProof);
    }

    /// <summary>Verifies that a ResynchronizeRequest round-trips.</summary>
    [Fact]
    public void RoundTrip_ResynchronizeRequest()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcResynchronizeRequestMessage(42);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a ResynchronizeResult round-trips for both accepted and declined outcomes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ResynchronizeResult(bool accepted)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcResynchronizeResultMessage(42, accepted);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a Close message round-trips for every defined close reason.</summary>
    [Theory]
    [InlineData(IpcCloseReason.Normal)]
    [InlineData(IpcCloseReason.Shutdown)]
    [InlineData(IpcCloseReason.Error)]
    public void RoundTrip_Close(IpcCloseReason reason)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcCloseMessage(0, reason);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a Reject message round-trips for every defined reject reason.</summary>
    [Theory]
    [InlineData(IpcRejectReason.MalformedFrameLength)]
    [InlineData(IpcRejectReason.UnknownMessageKind)]
    [InlineData(IpcRejectReason.InvalidIdentity)]
    [InlineData(IpcRejectReason.MalformedPayload)]
    [InlineData(IpcRejectReason.DuplicateTrustAdminCorrelationId)]
    public void RoundTrip_Reject(IpcRejectReason reason)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcRejectMessage(5, reason);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a Cancel message round-trips.</summary>
    [Fact]
    public void RoundTrip_Cancel()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcCancelMessage(5);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that an event-listening intent round-trips its opaque key.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrip_ListenEvent(uint eventKey)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcListenEventMessage(7, eventKey);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a sample-read intent round-trips its opaque token.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrip_ReadSample(uint sampleToken)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcReadSampleMessage(7, sampleToken);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that capture intents preserve the maximum correlation id.</summary>
    [Theory]
    [InlineData(IpcMessageKind.ListenEvent)]
    [InlineData(IpcMessageKind.ReadSample)]
    public void RoundTrip_CaptureIntent_PreservesMaximumCorrelationId(IpcMessageKind kind)
    {
        var codec = new IpcFrameCodec();
        IpcMessage original = kind == IpcMessageKind.ListenEvent
            ? new IpcListenEventMessage(ulong.MaxValue, 1)
            : new IpcReadSampleMessage(ulong.MaxValue, 1);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(ulong.MaxValue, result.Message!.CorrelationId);
    }

    /// <summary>Verifies that an event-listening intent requires nonzero identifiers.</summary>
    [Theory]
    [InlineData(0UL, 1u)]
    [InlineData(1UL, 0u)]
    public void Encode_ListenEvent_ZeroIdentifier_Throws(ulong correlationId, uint eventKey)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcListenEventMessage(correlationId, eventKey)));
    }

    /// <summary>Verifies that a sample-read intent requires nonzero identifiers.</summary>
    [Theory]
    [InlineData(0UL, 1u)]
    [InlineData(1UL, 0u)]
    public void Encode_ReadSample_ZeroIdentifier_Throws(ulong correlationId, uint sampleToken)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcReadSampleMessage(correlationId, sampleToken)));
    }

    /// <summary>Verifies that encoding a cancellation with zero correlation id fails closed.</summary>
    [Fact]
    public void Encode_Cancel_ZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcCancelMessage(0);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that a large, non-zero correlation id round-trips exactly.</summary>
    [Fact]
    public void RoundTrip_PreservesCorrelationId_NonZero()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcCancelMessage(ulong.MaxValue - 1);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(ulong.MaxValue - 1, result.Message!.CorrelationId);
    }

    /// <summary>Verifies that the maximum representable correlation id round-trips exactly.</summary>
    [Fact]
    public void RoundTrip_PreservesCorrelationId_MaxValue()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcCancelMessage(ulong.MaxValue);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(ulong.MaxValue, result.Message!.CorrelationId);
    }

    /// <summary>Verifies that a pairing-display request round-trips its code for every defined display mode.</summary>
    [Theory]
    [InlineData(PairingDisplayMode.Initial)]
    [InlineData(PairingDisplayMode.ManualRedisplay)]
    [InlineData(PairingDisplayMode.WrongCodeRedisplay)]
    public void RoundTrip_PairingDisplay(PairingDisplayMode mode)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcPairingDisplayMessage(7, "048372", mode);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a pairing-display acknowledgement round-trips for both outcomes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_PairingDisplayAck(bool accepted)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcPairingDisplayAckMessage(7, accepted);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that an attempts-exhausted notification round-trips.</summary>
    [Fact]
    public void RoundTrip_PairingAttemptsExhausted()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcPairingAttemptsExhaustedMessage(0);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a trust-admin request round-trips for every no-argument operation.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Help)]
    [InlineData(TrustAdminOperation.ResetTrust)]
    [InlineData(TrustAdminOperation.Reset)]
    public void RoundTrip_TrustAdminRequest_NoArgumentOperation(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminRequestMessage(10, operation);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a List request round-trips its scope for every defined scope.</summary>
    [Theory]
    [InlineData(TrustAdminListScope.All)]
    [InlineData(TrustAdminListScope.Trust)]
    [InlineData(TrustAdminListScope.Block)]
    public void RoundTrip_TrustAdminRequest_List(TrustAdminListScope scope)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminRequestMessage(10, TrustAdminOperation.List, ListScope: scope);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a device-targeted request round-trips its short id for every such operation.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Revoke)]
    [InlineData(TrustAdminOperation.Block)]
    [InlineData(TrustAdminOperation.Unblock)]
    [InlineData(TrustAdminOperation.Forget)]
    public void RoundTrip_TrustAdminRequest_ShortIdOperation(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminRequestMessage(10, operation, ShortId: "12345");

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a ConfirmReset request round-trips its six-digit confirmation code.</summary>
    [Fact]
    public void RoundTrip_TrustAdminRequest_ConfirmReset()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminRequestMessage(10, TrustAdminOperation.ConfirmReset, ConfirmationCode: "048372");

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a trust-admin result round-trips for empty, ordinary, and maximum-length text.</summary>
    [Fact]
    public void RoundTrip_TrustAdminResult()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminResultMessage(11, "Revoked device 12345.");

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a trust-admin result round-trips an empty result text.</summary>
    [Fact]
    public void RoundTrip_TrustAdminResult_EmptyText()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminResultMessage(11, string.Empty);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that a trust-admin result round-trips text at the maximum configured UTF-8 byte bound.</summary>
    [Fact]
    public void RoundTrip_TrustAdminResult_MaxLengthText()
    {
        var codec = new IpcFrameCodec();
        var original = new IpcTrustAdminResultMessage(11, new string('a', Constants.MaxIpcTrustAdminResultTextBytes));

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    // ---- Encode failures ----

    /// <summary>Verifies that encoding a Hello with an over-limit peer-proof token throws rather than producing a truncated frame.</summary>
    [Fact]
    public void Encode_Hello_OversizedToken_Throws()
    {
        var codec = new IpcFrameCodec();
        byte[] oversizedToken = new byte[Constants.MaxIpcPeerProofTokenBytes + 1];
        var message = new IpcHelloMessage(1, AdapterInstanceId.NewId(), oversizedToken);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that encoding a close with a nonzero correlation id fails closed.</summary>
    [Fact]
    public void Encode_Close_NonZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcCloseMessage(1, IpcCloseReason.Normal)));
    }

    /// <summary>Verifies that encoding a pairing-display request with a zero correlation id fails closed.</summary>
    [Fact]
    public void Encode_PairingDisplay_ZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingDisplayMessage(0, "123456", PairingDisplayMode.Initial)));
    }

    /// <summary>Verifies that encoding a pairing-display request with a code of the wrong length fails closed.</summary>
    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("")]
    public void Encode_PairingDisplay_WrongCodeLength_Throws(string code)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingDisplayMessage(1, code, PairingDisplayMode.Initial)));
    }

    /// <summary>Verifies that encoding a pairing-display request with a non-digit code fails closed.</summary>
    [Fact]
    public void Encode_PairingDisplay_NonDigitCode_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingDisplayMessage(1, "12a456", PairingDisplayMode.Initial)));
    }

    /// <summary>Verifies that encoding a pairing-display request with an unrecognized mode fails closed.</summary>
    [Fact]
    public void Encode_PairingDisplay_InvalidMode_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingDisplayMessage(1, "123456", (PairingDisplayMode)250)));
    }

    /// <summary>Verifies that encoding a pairing-display acknowledgement with a zero correlation id fails closed.</summary>
    [Fact]
    public void Encode_PairingDisplayAck_ZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingDisplayAckMessage(0, true)));
    }

    /// <summary>Verifies that encoding an attempts-exhausted notification with a nonzero correlation id fails closed.</summary>
    [Fact]
    public void Encode_PairingAttemptsExhausted_NonZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcPairingAttemptsExhaustedMessage(1)));
    }

    /// <summary>Verifies that encoding a trust-admin request with a zero correlation id fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_ZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(0, TrustAdminOperation.Help)));
    }

    /// <summary>Verifies that encoding a trust-admin request with an unrecognized operation fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_InvalidOperation_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, (TrustAdminOperation)250)));
    }

    /// <summary>Verifies that encoding a no-argument operation carrying an unrelated argument fails closed.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Help)]
    [InlineData(TrustAdminOperation.ResetTrust)]
    [InlineData(TrustAdminOperation.Reset)]
    public void Encode_TrustAdminRequest_NoArgumentOperation_WithUnrelatedArgument_Throws(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, operation, ShortId: "12345")));
    }

    /// <summary>Verifies that encoding a List request with no scope fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_List_MissingScope_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List)));
    }

    /// <summary>Verifies that encoding a List request carrying an unrelated argument alongside its scope fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_List_WithUnrelatedArgument_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() =>
            codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All, ShortId: "12345")));
    }

    /// <summary>Verifies that encoding a List request with an unrecognized scope fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_List_InvalidScope_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() =>
            codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: (TrustAdminListScope)250)));
    }

    /// <summary>Verifies that encoding a device-targeted operation with no short id fails closed.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Revoke)]
    [InlineData(TrustAdminOperation.Block)]
    [InlineData(TrustAdminOperation.Unblock)]
    [InlineData(TrustAdminOperation.Forget)]
    public void Encode_TrustAdminRequest_ShortIdOperation_MissingShortId_Throws(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, operation)));
    }

    /// <summary>Verifies that encoding a device-targeted operation with a short id of the wrong length fails closed, for every such operation.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Revoke, "1234")]
    [InlineData(TrustAdminOperation.Revoke, "123456")]
    [InlineData(TrustAdminOperation.Block, "1234")]
    [InlineData(TrustAdminOperation.Block, "123456")]
    [InlineData(TrustAdminOperation.Unblock, "1234")]
    [InlineData(TrustAdminOperation.Unblock, "123456")]
    [InlineData(TrustAdminOperation.Forget, "1234")]
    [InlineData(TrustAdminOperation.Forget, "123456")]
    public void Encode_TrustAdminRequest_ShortIdOperation_WrongLength_Throws(TrustAdminOperation operation, string shortId)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, operation, ShortId: shortId)));
    }

    /// <summary>Verifies that encoding a device-targeted operation with a non-digit short id fails closed, for every such operation.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Revoke)]
    [InlineData(TrustAdminOperation.Block)]
    [InlineData(TrustAdminOperation.Unblock)]
    [InlineData(TrustAdminOperation.Forget)]
    public void Encode_TrustAdminRequest_ShortIdOperation_NonDigit_Throws(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, operation, ShortId: "1a345")));
    }

    /// <summary>Verifies that encoding ConfirmReset with no confirmation code fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_ConfirmReset_MissingCode_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ConfirmReset)));
    }

    /// <summary>Verifies that encoding ConfirmReset with a confirmation code of the wrong length fails closed.</summary>
    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    public void Encode_TrustAdminRequest_ConfirmReset_WrongLength_Throws(string code)
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() =>
            codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ConfirmReset, ConfirmationCode: code)));
    }

    /// <summary>Verifies that encoding ConfirmReset with a non-digit confirmation code fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminRequest_ConfirmReset_NonDigit_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() =>
            codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ConfirmReset, ConfirmationCode: "04a372")));
    }

    /// <summary>Verifies that encoding a trust-admin result with a zero correlation id fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminResult_ZeroCorrelationId_Throws()
    {
        var codec = new IpcFrameCodec();

        Assert.Throws<ArgumentException>(() => codec.Encode(new IpcTrustAdminResultMessage(0, "text")));
    }

    /// <summary>Verifies that encoding a trust-admin result exceeding the configured length bound fails closed.</summary>
    [Fact]
    public void Encode_TrustAdminResult_OversizedText_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcTrustAdminResultMessage(1, new string('a', Constants.MaxIpcTrustAdminResultTextBytes + 1));

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    // ---- TryReadFrameLength ----

    /// <summary>Verifies that a length prefix at the minimum valid (header-only) value is accepted.</summary>
    [Fact]
    public void TryReadFrameLength_HeaderOnlyLength_ReturnsTrue()
    {
        var codec = new IpcFrameCodec();
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)Constants.IpcFrameHeaderBytes);

        bool ok = codec.TryReadFrameLength(prefix, out int frameLength);

        Assert.True(ok);
        Assert.Equal(Constants.IpcFrameHeaderBytes, frameLength);
    }

    /// <summary>Verifies that a length prefix declaring fewer bytes than one header is rejected before any payload read.</summary>
    [Fact]
    public void TryReadFrameLength_BelowHeaderSize_ReturnsFalse()
    {
        var codec = new IpcFrameCodec();
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)(Constants.IpcFrameHeaderBytes - 1));

        Assert.False(codec.TryReadFrameLength(prefix, out _));
    }

    /// <summary>Verifies that a length prefix declaring more than the configured limit is rejected without needing the payload bytes.</summary>
    [Fact]
    public void TryReadFrameLength_AboveMaxFrameBytes_ReturnsFalse()
    {
        var codec = new IpcFrameCodec();
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)Constants.MaxIpcFrameBytes + 1);

        Assert.False(codec.TryReadFrameLength(prefix, out _));
    }

    /// <summary>Verifies that a length prefix exactly at the configured limit is accepted.</summary>
    [Fact]
    public void TryReadFrameLength_AtMaxFrameBytes_ReturnsTrue()
    {
        var codec = new IpcFrameCodec();
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)Constants.MaxIpcFrameBytes);

        Assert.True(codec.TryReadFrameLength(prefix, out int frameLength));
        Assert.Equal(Constants.MaxIpcFrameBytes, frameLength);
    }

    /// <summary>Verifies that a length prefix of the wrong byte count is rejected.</summary>
    [Fact]
    public void TryReadFrameLength_WrongPrefixSize_ReturnsFalse()
    {
        var codec = new IpcFrameCodec();

        Assert.False(codec.TryReadFrameLength(new byte[3], out _));
        Assert.False(codec.TryReadFrameLength(new byte[5], out _));
    }

    // ---- Decode failures ----

    /// <summary>Verifies that a frame declaring an unrecognized message kind fails closed.</summary>
    [Fact]
    public void Decode_UnknownKind_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = codec.Encode(new IpcCancelMessage(1));
        frame[4] = 250;

        IpcDecodeResult result = codec.Decode(frame.AsSpan(4));

        Assert.Equal(IpcRejectReason.UnknownMessageKind, result.FailureReason);
    }

    /// <summary>Verifies that fewer bytes than one header fails closed rather than reading out of bounds.</summary>
    [Fact]
    public void Decode_TruncatedHeader_FailsClosed()
    {
        var codec = new IpcFrameCodec();

        IpcDecodeResult result = codec.Decode(new byte[Constants.IpcFrameHeaderBytes - 1]);

        Assert.Equal(IpcRejectReason.MalformedFrameLength, result.FailureReason);
    }

    /// <summary>Verifies that Decode independently rejects a frame longer than the configured limit, even if it was never passed through <see cref="IIpcFrameCodec.TryReadFrameLength"/>.</summary>
    [Fact]
    public void Decode_OversizedFrame_FailsClosed()
    {
        var codec = new IpcFrameCodec();

        IpcDecodeResult result = codec.Decode(new byte[Constants.MaxIpcFrameBytes + 1]);

        Assert.Equal(IpcRejectReason.MalformedFrameLength, result.FailureReason);
    }

    /// <summary>Verifies that a Hello payload of the wrong length fails closed as a malformed payload.</summary>
    [Fact]
    public void Decode_Hello_WrongPayloadLength_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [9, 9]));
        Array.Resize(ref frame, frame.Length - 1);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)(frame.Length - 4));

        IpcDecodeResult result = codec.Decode(frame.AsSpan(4));

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Hello payload one byte short of the minimum (missing the token-length byte) fails closed.</summary>
    [Fact]
    public void Decode_Hello_PayloadExactly16Bytes_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Hello, correlationId: 1, new byte[16]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Hello payload whose declared token length exceeds the bound fails closed as an invalid identity.</summary>
    [Fact]
    public void Decode_Hello_TokenLengthExceedsBound_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = new byte[17];
        payload[16] = unchecked((byte)(Constants.MaxIpcPeerProofTokenBytes + 1));
        byte[] frame = BuildFrame(IpcMessageKind.Hello, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.InvalidIdentity, result.FailureReason);
    }

    /// <summary>Verifies that a HelloAck payload with an out-of-range accepted byte fails closed.</summary>
    [Fact]
    public void Decode_HelloAck_InvalidAcceptedByte_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1, [2, 0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that an accepted HelloAck cannot carry a rejection reason.</summary>
    [Fact]
    public void Decode_HelloAck_AcceptedWithRejectReason_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1,
            new byte[] { 1, (byte)IpcHelloRejectReason.InvalidProof });

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a rejected HelloAck must carry a rejection reason.</summary>
    [Fact]
    public void Decode_HelloAck_RejectedWithoutRejectReason_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1,
            new byte[] { 0, (byte)IpcHelloRejectReason.None });

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a HelloAck payload with an unrecognized reject reason fails closed.</summary>
    [Fact]
    public void Decode_HelloAck_UnknownRejectReason_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1, [0, 250]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a HelloAck payload of the wrong length fails closed, both shorter and longer than the fixed 2-byte shape.</summary>
    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 1, 0 })]
    public void Decode_HelloAck_WrongPayloadLength_FailsClosed(byte[] payload)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.HelloAck, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a HelloAck payload one byte short or one byte long of the fixed 34-byte shape fails closed.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Decode_HelloAck_PayloadOffByOneFromHostProofBoundary_FailsClosed(int bytesBeyondCorrectLength)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(
            IpcMessageKind.HelloAck, correlationId: 1, new byte[2 + Constants.IpcHostProofBytes + bytesBeyondCorrectLength]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a ResynchronizeRequest carrying an unexpected payload fails closed.</summary>
    [Fact]
    public void Decode_ResynchronizeRequest_NonEmptyPayload_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ResynchronizeRequest, correlationId: 1, [0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a ResynchronizeResult payload with an out-of-range accepted byte fails closed.</summary>
    [Fact]
    public void Decode_ResynchronizeResult_InvalidAcceptedByte_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ResynchronizeResult, correlationId: 1, [2]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Close payload with an unrecognized reason fails closed.</summary>
    [Fact]
    public void Decode_Close_UnknownReason_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Close, correlationId: 0, [250]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a close carrying a correlation id fails closed because close is unsolicited.</summary>
    [Fact]
    public void Decode_Close_NonZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Close, correlationId: 1, new byte[] { (byte)IpcCloseReason.Normal });

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Close payload of the wrong length fails closed, both shorter and longer than the fixed 1-byte shape.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 0 })]
    public void Decode_Close_WrongPayloadLength_FailsClosed(byte[] payload)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Close, correlationId: 0, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Reject payload with an unrecognized reason fails closed.</summary>
    [Fact]
    public void Decode_Reject_UnknownReason_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Reject, correlationId: 1, [250]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Reject payload of the wrong length fails closed, both shorter and longer than the fixed 1-byte shape.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 0 })]
    public void Decode_Reject_WrongPayloadLength_FailsClosed(byte[] payload)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Reject, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a Cancel message carrying an unexpected payload fails closed.</summary>
    [Fact]
    public void Decode_Cancel_NonEmptyPayload_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Cancel, correlationId: 1, [0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a cancellation without a target request fails closed.</summary>
    [Fact]
    public void Decode_Cancel_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.Cancel, correlationId: 0, Array.Empty<byte>());

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that an event-listening intent with zero correlation fails closed.</summary>
    [Fact]
    public void Decode_ListenEvent_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ListenEvent, correlationId: 0, BitConverter.GetBytes(1u));

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that an event-listening intent with zero key fails closed.</summary>
    [Fact]
    public void Decode_ListenEvent_ZeroKey_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ListenEvent, correlationId: 1, new byte[sizeof(uint)]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a sample-read intent with zero correlation fails closed.</summary>
    [Fact]
    public void Decode_ReadSample_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ReadSample, correlationId: 0, BitConverter.GetBytes(1u));

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a sample-read intent with zero token fails closed.</summary>
    [Fact]
    public void Decode_ReadSample_ZeroToken_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.ReadSample, correlationId: 1, new byte[sizeof(uint)]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that event-listening and sample-read intents require exactly four payload bytes.</summary>
    [Theory]
    [InlineData(IpcMessageKind.ListenEvent, 0)]
    [InlineData(IpcMessageKind.ListenEvent, 5)]
    [InlineData(IpcMessageKind.ReadSample, 0)]
    [InlineData(IpcMessageKind.ReadSample, 5)]
    public void Decode_CaptureIntent_WrongPayloadLength_FailsClosed(IpcMessageKind kind, int payloadLength)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(kind, correlationId: 1, new byte[payloadLength]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display request with a zero correlation id fails closed.</summary>
    [Fact]
    public void Decode_PairingDisplay_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = [(byte)PairingDisplayMode.Initial, (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6'];
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplay, correlationId: 0, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display request with an unrecognized mode fails closed.</summary>
    [Fact]
    public void Decode_PairingDisplay_UnknownMode_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = [250, (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6'];
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplay, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display request with a non-digit code byte fails closed.</summary>
    [Fact]
    public void Decode_PairingDisplay_NonDigitCode_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = [(byte)PairingDisplayMode.Initial, (byte)'1', (byte)'2', (byte)'a', (byte)'4', (byte)'5', (byte)'6'];
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplay, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display request payload of the wrong length fails closed, both shorter and longer than the fixed 7-byte shape.</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Decode_PairingDisplay_WrongPayloadLength_FailsClosed(int payloadLength)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplay, correlationId: 1, new byte[payloadLength]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display acknowledgement with a zero correlation id fails closed.</summary>
    [Fact]
    public void Decode_PairingDisplayAck_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplayAck, correlationId: 0, [1]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display acknowledgement with an out-of-range accepted byte fails closed.</summary>
    [Fact]
    public void Decode_PairingDisplayAck_InvalidAcceptedByte_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplayAck, correlationId: 1, [2]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a pairing-display acknowledgement payload of the wrong length fails closed, both shorter and longer than the fixed 1-byte shape.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 0 })]
    public void Decode_PairingDisplayAck_WrongPayloadLength_FailsClosed(byte[] payload)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingDisplayAck, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that an attempts-exhausted notification carrying a correlation id fails closed because it is unsolicited.</summary>
    [Fact]
    public void Decode_PairingAttemptsExhausted_NonZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingAttemptsExhausted, correlationId: 1, Array.Empty<byte>());

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that an attempts-exhausted notification carrying an unexpected payload fails closed.</summary>
    [Fact]
    public void Decode_PairingAttemptsExhausted_NonEmptyPayload_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.PairingAttemptsExhausted, correlationId: 0, [0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin request with a zero correlation id fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 0, [(byte)TrustAdminOperation.Help]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin request with an empty payload (missing the operation byte) fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_EmptyPayload_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, Array.Empty<byte>());

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin request with an unrecognized operation byte fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_UnknownOperation_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, [250]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a no-argument operation carrying an unexpected trailing byte fails closed.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Help)]
    [InlineData(TrustAdminOperation.ResetTrust)]
    [InlineData(TrustAdminOperation.Reset)]
    public void Decode_TrustAdminRequest_NoArgumentOperation_NonEmptyArgument_FailsClosed(TrustAdminOperation operation)
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, [(byte)operation, 0]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a List request's argument of the wrong length fails closed, both shorter and longer than the fixed one-byte shape.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 0 })]
    public void Decode_TrustAdminRequest_List_WrongArgumentLength_FailsClosed(byte[] argument)
    {
        var codec = new IpcFrameCodec();
        byte[] payload = new byte[1 + argument.Length];
        payload[0] = (byte)TrustAdminOperation.List;
        argument.CopyTo(payload, 1);
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a List request with an unrecognized scope byte fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_List_UnknownScope_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, [(byte)TrustAdminOperation.List, 250]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a device-targeted operation's short-id argument of the wrong length fails closed.</summary>
    [Theory]
    [InlineData(TrustAdminOperation.Revoke, 4)]
    [InlineData(TrustAdminOperation.Revoke, 6)]
    [InlineData(TrustAdminOperation.Block, 4)]
    [InlineData(TrustAdminOperation.Unblock, 4)]
    [InlineData(TrustAdminOperation.Forget, 4)]
    public void Decode_TrustAdminRequest_ShortIdOperation_WrongArgumentLength_FailsClosed(TrustAdminOperation operation, int argumentLength)
    {
        var codec = new IpcFrameCodec();
        byte[] payload = new byte[1 + argumentLength];
        payload[0] = (byte)operation;
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a device-targeted operation's non-digit short-id argument fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_ShortIdOperation_NonDigit_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = [(byte)TrustAdminOperation.Revoke, (byte)'1', (byte)'2', (byte)'a', (byte)'4', (byte)'5'];
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that ConfirmReset's confirmation-code argument of the wrong length fails closed.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Decode_TrustAdminRequest_ConfirmReset_WrongArgumentLength_FailsClosed(int argumentLength)
    {
        var codec = new IpcFrameCodec();
        byte[] payload = new byte[1 + argumentLength];
        payload[0] = (byte)TrustAdminOperation.ConfirmReset;
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that ConfirmReset's non-digit confirmation-code argument fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminRequest_ConfirmReset_NonDigit_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] payload = [(byte)TrustAdminOperation.ConfirmReset, (byte)'0', (byte)'4', (byte)'a', (byte)'3', (byte)'7', (byte)'2'];
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminRequest, correlationId: 1, payload);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin result with a zero correlation id fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminResult_ZeroCorrelationId_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminResult, correlationId: 0, [(byte)'O', (byte)'K']);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin result exceeding the configured length bound fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminResult_OversizedPayload_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminResult, correlationId: 1, new byte[Constants.MaxIpcTrustAdminResultTextBytes + 1]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies that a trust-admin result carrying invalid UTF-8 bytes fails closed.</summary>
    [Fact]
    public void Decode_TrustAdminResult_InvalidUtf8_FailsClosed()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = BuildFrame(IpcMessageKind.TrustAdminResult, correlationId: 1, [0xFF, 0xFE]);

        IpcDecodeResult result = codec.Decode(frame);

        Assert.Equal(IpcRejectReason.MalformedPayload, result.FailureReason);
    }

    /// <summary>Verifies exact no-version wire bytes for every current private IPC message kind.</summary>
    [Fact]
    public void GoldenVectors_EncodeAndDecodeWithTheSharedWireLayout()
    {
        var codec = new IpcFrameCodec();
        var adapterInstanceId = new AdapterInstanceId(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        var vectors = new (IpcMessage Message, string Hex)[]
        {
            // 64-byte Hello payload: 16 identity + 1 token-length + 3 token + 32 zero-filled challenge +
            // 12 zero-filled ownerLifetimeId (this vector's IpcHelloMessage does not set either field).
            (new IpcHelloMessage(1, adapterInstanceId, [0xA0, 0xB1, 0xC2]),
                "4900000001010000000000000000112233445566778899AABBCCDDEEFF03A0B1C2" +
                "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"),
            // 34-byte HelloAck payload: accepted + rejectReason + 32 zero-filled hostProof (this
            // vector's IpcHelloAckMessage does not set hostProof).
            (new IpcHelloAckMessage(2, true, IpcHelloRejectReason.None),
                "2B0000000202000000000000000100" +
                "0000000000000000000000000000000000000000000000000000000000000000"),
            (new IpcResynchronizeRequestMessage(4), "09000000030400000000000000"),
            (new IpcResynchronizeResultMessage(5, Accepted: true), "0A00000004050000000000000001"),
            (new IpcCloseMessage(0, IpcCloseReason.Normal), "0A00000005000000000000000000"),
            (new IpcRejectMessage(6, IpcRejectReason.InvalidIdentity), "0A00000006060000000000000002"),
            (new IpcCancelMessage(7), "09000000070700000000000000"),
            (new IpcListenEventMessage(0x0102030405060708, 0x0A0B0C0D), "0D0000000808070605040302010D0C0B0A"),
            (new IpcReadSampleMessage(0x1122334455667788, 0xA1B2C3D4), "0D000000098877665544332211D4C3B2A1"),
            // 7-byte PairingDisplay payload: 1 mode byte + 6 ASCII code digits.
            (new IpcPairingDisplayMessage(8, "123456", PairingDisplayMode.Initial),
                "100000000A080000000000000000313233343536"),
            (new IpcPairingDisplayAckMessage(9, Accepted: true), "0A0000000B090000000000000001"),
            (new IpcPairingAttemptsExhaustedMessage(0), "090000000C0000000000000000"),
            // 6-byte TrustAdminRequest payload: 1 operation byte (Revoke) + 5 ASCII short-id digits.
            (new IpcTrustAdminRequestMessage(10, TrustAdminOperation.Revoke, ShortId: "12345"),
                "0F0000000D0A00000000000000023132333435"),
            (new IpcTrustAdminResultMessage(11, "OK"), "0B0000000E0B000000000000004F4B"),
        };

        foreach ((IpcMessage message, string hex) in vectors)
        {
            byte[] expected = Convert.FromHexString(hex);
            byte[] encoded = codec.Encode(message);
            Assert.Equal(expected, encoded);

            IpcDecodeResult decoded = codec.Decode(expected.AsSpan(4));
            Assert.Null(decoded.FailureReason);
            Assert.IsType(message.GetType(), decoded.Message);
            Assert.Equal(message.CorrelationId, decoded.Message!.CorrelationId);
            switch ((message, decoded.Message))
            {
                case (IpcHelloMessage expectedMessage, IpcHelloMessage actualMessage):
                    Assert.Equal(expectedMessage.AdapterInstanceId, actualMessage.AdapterInstanceId);
                    Assert.Equal(expectedMessage.PeerProofToken, actualMessage.PeerProofToken);
                    break;
                case (IpcHelloAckMessage expectedMessage, IpcHelloAckMessage actualMessage):
                    Assert.Equal(expectedMessage.Accepted, actualMessage.Accepted);
                    Assert.Equal(expectedMessage.RejectReason, actualMessage.RejectReason);
                    break;
                case (IpcResynchronizeResultMessage expectedMessage, IpcResynchronizeResultMessage actualMessage):
                    Assert.Equal(expectedMessage.Accepted, actualMessage.Accepted);
                    break;
                case (IpcCloseMessage expectedMessage, IpcCloseMessage actualMessage):
                    Assert.Equal(expectedMessage.Reason, actualMessage.Reason);
                    break;
                case (IpcRejectMessage expectedMessage, IpcRejectMessage actualMessage):
                    Assert.Equal(expectedMessage.Reason, actualMessage.Reason);
                    break;
                case (IpcListenEventMessage expectedMessage, IpcListenEventMessage actualMessage):
                    Assert.Equal(expectedMessage.EventKey, actualMessage.EventKey);
                    break;
                case (IpcReadSampleMessage expectedMessage, IpcReadSampleMessage actualMessage):
                    Assert.Equal(expectedMessage.SampleToken, actualMessage.SampleToken);
                    break;
                case (IpcPairingDisplayMessage expectedMessage, IpcPairingDisplayMessage actualMessage):
                    Assert.Equal(expectedMessage.Code, actualMessage.Code);
                    Assert.Equal(expectedMessage.Mode, actualMessage.Mode);
                    break;
                case (IpcPairingDisplayAckMessage expectedMessage, IpcPairingDisplayAckMessage actualMessage):
                    Assert.Equal(expectedMessage.Accepted, actualMessage.Accepted);
                    break;
                case (IpcTrustAdminRequestMessage expectedMessage, IpcTrustAdminRequestMessage actualMessage):
                    Assert.Equal(expectedMessage.Operation, actualMessage.Operation);
                    Assert.Equal(expectedMessage.ShortId, actualMessage.ShortId);
                    break;
                case (IpcTrustAdminResultMessage expectedMessage, IpcTrustAdminResultMessage actualMessage):
                    Assert.Equal(expectedMessage.ResultText, actualMessage.ResultText);
                    break;
                case (IpcResynchronizeRequestMessage, IpcResynchronizeRequestMessage):
                case (IpcCancelMessage, IpcCancelMessage):
                case (IpcPairingAttemptsExhaustedMessage, IpcPairingAttemptsExhaustedMessage):
                    break;
                default:
                    Assert.Fail("The decoded message shape did not match the golden vector.");
                    break;
            }
        }
    }

    // ---- Idempotence and robustness ----

    /// <summary>Verifies that decoding the same Close frame repeatedly is side-effect-free and never throws.</summary>
    [Fact]
    public void Decode_Close_Repeated_IsIdempotentAndNeverThrows()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));

        IpcDecodeResult first = codec.Decode(frame.AsSpan(4));
        IpcDecodeResult second = codec.Decode(frame.AsSpan(4));

        Assert.Equal(first, second);
    }

    /// <summary>Verifies that decoding the same Cancel frame repeatedly is side-effect-free and never throws.</summary>
    [Fact]
    public void Decode_Cancel_Repeated_IsIdempotentAndNeverThrows()
    {
        var codec = new IpcFrameCodec();
        byte[] frame = codec.Encode(new IpcCancelMessage(3));

        IpcDecodeResult first = codec.Decode(frame.AsSpan(4));
        IpcDecodeResult second = codec.Decode(frame.AsSpan(4));

        Assert.Equal(first, second);
    }

    /// <summary>Verifies that decoding arbitrary, entirely untrusted byte content never throws.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 250, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3 })]
    public void Decode_GarbageBytes_NeverThrows(byte[] garbage)
    {
        var codec = new IpcFrameCodec();

        IpcDecodeResult result = codec.Decode(garbage);

        Assert.NotNull(result);
    }

    /// <summary>Builds a frame's header-plus-payload bytes directly, for constructing wire content the codec's own Encode cannot produce.</summary>
    private static byte[] BuildFrame(IpcMessageKind kind, ulong correlationId, byte[] payload)
    {
        var frame = new byte[Constants.IpcFrameHeaderBytes + payload.Length];
        frame[0] = (byte)kind;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(1, 8), correlationId);
        payload.CopyTo(frame, Constants.IpcFrameHeaderBytes);
        return frame;
    }
}
