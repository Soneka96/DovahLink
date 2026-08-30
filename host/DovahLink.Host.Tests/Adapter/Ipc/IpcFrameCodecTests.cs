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
        var original = new IpcHelloAckMessage(1, Accepted: true, IpcHelloRejectReason.None);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
    }

    /// <summary>Verifies that encoding an accepted HelloAck with a rejection reason fails closed.</summary>
    [Fact]
    public void Encode_HelloAck_AcceptedWithRejectReason_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcHelloAckMessage(1, Accepted: true, IpcHelloRejectReason.InvalidProof);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that encoding a rejected HelloAck without a rejection reason fails closed.</summary>
    [Fact]
    public void Encode_HelloAck_RejectedWithoutRejectReason_Throws()
    {
        var codec = new IpcFrameCodec();
        var message = new IpcHelloAckMessage(1, Accepted: false, IpcHelloRejectReason.None);

        Assert.Throws<ArgumentException>(() => codec.Encode(message));
    }

    /// <summary>Verifies that a rejected HelloAck round-trips for every non-<see cref="IpcHelloRejectReason.None"/> reason.</summary>
    [Theory]
    [InlineData(IpcHelloRejectReason.InvalidProof)]
    [InlineData(IpcHelloRejectReason.Malformed)]
    public void RoundTrip_HelloAck_Rejected(IpcHelloRejectReason reason)
    {
        var codec = new IpcFrameCodec();
        var original = new IpcHelloAckMessage(1, Accepted: false, reason);

        (IpcDecodeResult result, _) = EncodeThenDecode(codec, original);

        Assert.Equal(original, result.Message);
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
