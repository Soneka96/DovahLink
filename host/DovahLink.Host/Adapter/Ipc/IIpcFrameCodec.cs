using System.Buffers.Binary;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Encodes and decodes private host-to-adapter IPC frames. The wire layout is:
/// a 4-byte little-endian frame length (the byte count of everything after this field), a 1-byte
/// message kind, an 8-byte little-endian correlation id, then a kind-specific payload. Host and
/// adapter are shipped as one package; peer ownership and lifetime proof determine whether the
/// connection belongs to this installation. This codec performs no I/O; it operates on already-read
/// frame bytes and produces owned plain values only.
/// </summary>
public interface IIpcFrameCodec
{
    /// <summary>Encodes a message into a complete frame, including its length prefix.</summary>
    /// <param name="message">The message to encode.</param>
    byte[] Encode(IpcMessage message);

    /// <summary>
    /// Reads and validates a frame's 4-byte length prefix before any payload bytes are read or
    /// allocated, so an over-limit declared length is rejected without allocating a buffer for it.
    /// </summary>
    /// <param name="lengthPrefix">Exactly 4 bytes: the frame's length prefix.</param>
    /// <param name="frameLength">The validated header-plus-payload byte length, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the declared length is well-formed and within limits.</returns>
    bool TryReadFrameLength(ReadOnlySpan<byte> lengthPrefix, out int frameLength);

    /// <summary>Decodes one frame's header-plus-payload bytes (excluding the length prefix) into a message.</summary>
    /// <param name="frame">The header-plus-payload bytes, as validated by <see cref="TryReadFrameLength"/>.</param>
    IpcDecodeResult Decode(ReadOnlySpan<byte> frame);
}

/// <inheritdoc cref="IIpcFrameCodec"/>
public sealed class IpcFrameCodec : IIpcFrameCodec
{
    /// <inheritdoc/>
    public byte[] Encode(IpcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        (IpcMessageKind kind, byte[] payload) = message switch
        {
            IpcHelloMessage hello => (IpcMessageKind.Hello, EncodeHello(hello)),
            IpcHelloAckMessage helloAck => (IpcMessageKind.HelloAck, EncodeHelloAck(helloAck)),
            IpcResynchronizeRequestMessage => (IpcMessageKind.ResynchronizeRequest, Array.Empty<byte>()),
            IpcResynchronizeResultMessage resynchronizeResult =>
                (IpcMessageKind.ResynchronizeResult, new byte[] { resynchronizeResult.Accepted ? (byte)1 : (byte)0 }),
            IpcCloseMessage close => (IpcMessageKind.Close, EncodeClose(close)),
            IpcRejectMessage reject => (IpcMessageKind.Reject, new byte[] { (byte)reject.Reason }),
            IpcCancelMessage cancel => (IpcMessageKind.Cancel, EncodeCancel(cancel)),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message, "Unrecognized IPC message type."),
        };

        int totalLength = Constants.IpcFrameHeaderBytes + payload.Length;
        var frame = new byte[sizeof(uint) + totalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)totalLength);
        frame[4] = (byte)kind;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(5, 8), message.CorrelationId);
        payload.CopyTo(frame.AsSpan(Constants.IpcFrameHeaderBytes + sizeof(uint)));
        return frame;
    }

    /// <inheritdoc/>
    public bool TryReadFrameLength(ReadOnlySpan<byte> lengthPrefix, out int frameLength)
    {
        if (lengthPrefix.Length != sizeof(uint))
        {
            frameLength = 0;
            return false;
        }

        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthPrefix);
        if (declaredLength < Constants.IpcFrameHeaderBytes || declaredLength > Constants.MaxIpcFrameBytes)
        {
            frameLength = 0;
            return false;
        }

        frameLength = (int)declaredLength;
        return true;
    }

    /// <inheritdoc/>
    public IpcDecodeResult Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Constants.IpcFrameHeaderBytes || frame.Length > Constants.MaxIpcFrameBytes)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedFrameLength);
        }

        byte kindByte = frame[0];
        if (!Enum.IsDefined((IpcMessageKind)kindByte))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.UnknownMessageKind);
        }

        ulong correlationId = BinaryPrimitives.ReadUInt64LittleEndian(frame.Slice(1, sizeof(ulong)));
        ReadOnlySpan<byte> payload = frame[Constants.IpcFrameHeaderBytes..];

        return (IpcMessageKind)kindByte switch
        {
            IpcMessageKind.Hello => DecodeHello(correlationId, payload),
            IpcMessageKind.HelloAck => DecodeHelloAck(correlationId, payload),
            IpcMessageKind.ResynchronizeRequest => payload.IsEmpty
                ? IpcDecodeResult.Success(new IpcResynchronizeRequestMessage(correlationId))
                : IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload),
            IpcMessageKind.ResynchronizeResult => DecodeResynchronizeResult(correlationId, payload),
            IpcMessageKind.Close => DecodeClose(correlationId, payload),
            IpcMessageKind.Reject => DecodeReject(correlationId, payload),
            IpcMessageKind.Cancel => correlationId == 0 || !payload.IsEmpty
                ? IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload)
                : IpcDecodeResult.Success(new IpcCancelMessage(correlationId)),
            _ => IpcDecodeResult.Failure(IpcRejectReason.UnknownMessageKind),
        };
    }

    /// <summary>Encodes an <see cref="IpcHelloMessage"/> payload: 16 identity bytes, a length byte, then the token.</summary>
    private static byte[] EncodeHello(IpcHelloMessage hello)
    {
        if (hello.PeerProofToken.Length > Constants.MaxIpcPeerProofTokenBytes)
        {
            throw new ArgumentException(
                $"The peer-proof token must be at most {Constants.MaxIpcPeerProofTokenBytes} bytes.", nameof(hello));
        }

        var payload = new byte[17 + hello.PeerProofToken.Length];
        hello.AdapterInstanceId.Value.TryWriteBytes(payload.AsSpan(0, 16), bigEndian: true, out _);
        payload[16] = (byte)hello.PeerProofToken.Length;
        hello.PeerProofToken.CopyTo(payload.AsSpan(17));
        return payload;
    }

    /// <summary>Encodes an <see cref="IpcHelloAckMessage"/> payload: accepted and reject reason.</summary>
    private static byte[] EncodeHelloAck(IpcHelloAckMessage helloAck)
    {
        ValidateHelloAck(helloAck);
        return [helloAck.Accepted ? (byte)1 : (byte)0, (byte)helloAck.RejectReason];
    }

    /// <summary>Validates the semantic relationship between HelloAck fields.</summary>
    /// <param name="helloAck">The acknowledgement to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the acknowledgement is inconsistent with the supported protocol.</exception>
    private static void ValidateHelloAck(IpcHelloAckMessage helloAck)
    {
        bool hasRejectReason = helloAck.RejectReason != IpcHelloRejectReason.None;
        if (helloAck.Accepted == hasRejectReason)
        {
            throw new ArgumentException("An accepted HelloAck must have no reject reason, and a rejected HelloAck must have one.", nameof(helloAck));
        }
    }

    /// <summary>Encodes a close message after enforcing its unsolicited-message correlation rule.</summary>
    /// <param name="close">The close message to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the close carries a nonzero correlation id.</exception>
    private static byte[] EncodeClose(IpcCloseMessage close)
    {
        if (close.CorrelationId != 0)
        {
            throw new ArgumentException("A close message must have correlation id zero.", nameof(close));
        }

        return [(byte)close.Reason];
    }

    /// <summary>Encodes a cancellation after enforcing its required request correlation.</summary>
    /// <param name="cancel">The cancellation message to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the cancellation has correlation id zero.</exception>
    private static byte[] EncodeCancel(IpcCancelMessage cancel)
    {
        if (cancel.CorrelationId == 0)
        {
            throw new ArgumentException("A cancel message must identify a nonzero request correlation id.", nameof(cancel));
        }

        return Array.Empty<byte>();
    }

    /// <summary>Decodes an <see cref="IpcHelloMessage"/> payload, validating the bounded token length.</summary>
    private static IpcDecodeResult DecodeHello(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 17)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        int tokenLength = payload[16];
        if (tokenLength > Constants.MaxIpcPeerProofTokenBytes)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.InvalidIdentity);
        }

        if (payload.Length != 17 + tokenLength)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        var instanceId = new AdapterInstanceId(new Guid(payload[..16], bigEndian: true));
        byte[] token = payload.Slice(17, tokenLength).ToArray();
        return IpcDecodeResult.Success(new IpcHelloMessage(correlationId, instanceId, token));
    }

    /// <summary>Decodes an <see cref="IpcHelloAckMessage"/> payload, validating its boolean and enum fields.</summary>
    private static IpcDecodeResult DecodeHelloAck(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 2 || payload[0] > 1 || !Enum.IsDefined((IpcHelloRejectReason)payload[1]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        bool accepted = payload[0] == 1;
        bool hasRejectReason = (IpcHelloRejectReason)payload[1] != IpcHelloRejectReason.None;
        if (accepted == hasRejectReason)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(
            new IpcHelloAckMessage(correlationId, accepted, (IpcHelloRejectReason)payload[1]));
    }

    /// <summary>Decodes an <see cref="IpcResynchronizeResultMessage"/> payload, validating its boolean field.</summary>
    private static IpcDecodeResult DecodeResynchronizeResult(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1 || payload[0] > 1)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcResynchronizeResultMessage(correlationId, payload[0] == 1));
    }

    /// <summary>Decodes an <see cref="IpcCloseMessage"/> payload, validating its reason enum.</summary>
    private static IpcDecodeResult DecodeClose(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId != 0 || payload.Length != 1 || !Enum.IsDefined((IpcCloseReason)payload[0]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcCloseMessage(correlationId, (IpcCloseReason)payload[0]));
    }

    /// <summary>Decodes an <see cref="IpcRejectMessage"/> payload, validating its reason enum.</summary>
    private static IpcDecodeResult DecodeReject(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1 || !Enum.IsDefined((IpcRejectReason)payload[0]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcRejectMessage(correlationId, (IpcRejectReason)payload[0]));
    }
}
