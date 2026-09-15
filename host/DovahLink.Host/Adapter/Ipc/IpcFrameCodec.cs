using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;
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
            IpcListenEventMessage listenEvent => (IpcMessageKind.ListenEvent, EncodeListenEvent(listenEvent)),
            IpcReadSampleMessage readSample => (IpcMessageKind.ReadSample, EncodeReadSample(readSample)),
            IpcPairingDisplayMessage pairingDisplay => (IpcMessageKind.PairingDisplay, EncodePairingDisplay(pairingDisplay)),
            IpcPairingDisplayAckMessage pairingDisplayAck => (IpcMessageKind.PairingDisplayAck, EncodePairingDisplayAck(pairingDisplayAck)),
            IpcPairingAttemptsExhaustedMessage pairingAttemptsExhausted =>
                (IpcMessageKind.PairingAttemptsExhausted, EncodePairingAttemptsExhausted(pairingAttemptsExhausted)),
            IpcTrustAdminRequestMessage trustAdminRequest => (IpcMessageKind.TrustAdminRequest, EncodeTrustAdminRequest(trustAdminRequest)),
            IpcTrustAdminResultMessage trustAdminResult => (IpcMessageKind.TrustAdminResult, EncodeTrustAdminResult(trustAdminResult)),
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
            IpcMessageKind.ListenEvent => DecodeListenEvent(correlationId, payload),
            IpcMessageKind.ReadSample => DecodeReadSample(correlationId, payload),
            IpcMessageKind.PairingDisplay => DecodePairingDisplay(correlationId, payload),
            IpcMessageKind.PairingDisplayAck => DecodePairingDisplayAck(correlationId, payload),
            IpcMessageKind.PairingAttemptsExhausted => DecodePairingAttemptsExhausted(correlationId, payload),
            IpcMessageKind.TrustAdminRequest => DecodeTrustAdminRequest(correlationId, payload),
            IpcMessageKind.TrustAdminResult => DecodeTrustAdminResult(correlationId, payload),
            _ => IpcDecodeResult.Failure(IpcRejectReason.UnknownMessageKind),
        };
    }

    /// <summary>
    /// Encodes an <see cref="IpcHelloMessage"/> payload: 16 identity bytes, a length byte, the token,
    /// then the fixed-size challenge and owner-lifetime-id fields.
    /// </summary>
    private static byte[] EncodeHello(IpcHelloMessage hello)
    {
        byte[] peerProofToken = hello.PeerProofToken;
        if (peerProofToken.Length > Constants.MaxIpcPeerProofTokenBytes)
        {
            throw new ArgumentException(
                $"The peer-proof token must be at most {Constants.MaxIpcPeerProofTokenBytes} bytes.", nameof(hello));
        }

        int challengeOffset = 17 + peerProofToken.Length;
        int ownerLifetimeIdOffset = challengeOffset + Constants.IpcChallengeBytes;
        var payload = new byte[ownerLifetimeIdOffset + Constants.IpcOwnerLifetimeIdBytes];
        hello.AdapterInstanceId.Value.TryWriteBytes(payload.AsSpan(0, 16), bigEndian: true, out _);
        payload[16] = (byte)peerProofToken.Length;
        peerProofToken.CopyTo(payload.AsSpan(17));
        hello.Challenge.CopyTo(payload.AsSpan(challengeOffset));
        hello.OwnerLifetimeId.CopyTo(payload.AsSpan(ownerLifetimeIdOffset));
        return payload;
    }

    /// <summary>Encodes an <see cref="IpcHelloAckMessage"/> payload: accepted, reject reason, then the fixed-size host proof.</summary>
    private static byte[] EncodeHelloAck(IpcHelloAckMessage helloAck)
    {
        ValidateHelloAck(helloAck);
        var payload = new byte[2 + Constants.IpcHostProofBytes];
        payload[0] = helloAck.Accepted ? (byte)1 : (byte)0;
        payload[1] = (byte)helloAck.RejectReason;
        helloAck.HostProof.CopyTo(payload.AsSpan(2));
        return payload;
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

    /// <summary>Encodes a host-owned event key as one little-endian 32-bit value.</summary>
    /// <param name="listenEvent">The event-listening request to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the request has no correlation or key.</exception>
    private static byte[] EncodeListenEvent(IpcListenEventMessage listenEvent)
    {
        ValidateCaptureIntent(listenEvent.CorrelationId, listenEvent.EventKey, nameof(listenEvent));
        byte[] payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, listenEvent.EventKey);
        return payload;
    }

    /// <summary>Encodes a host-owned sample token as one little-endian 32-bit value.</summary>
    /// <param name="readSample">The sample-read request to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the request has no correlation or token.</exception>
    private static byte[] EncodeReadSample(IpcReadSampleMessage readSample)
    {
        ValidateCaptureIntent(readSample.CorrelationId, readSample.SampleToken, nameof(readSample));
        byte[] payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, readSample.SampleToken);
        return payload;
    }

    /// <summary>Validates the common identity rules for host-directed capture intents.</summary>
    /// <param name="correlationId">The request correlation id.</param>
    /// <param name="intentId">The event key or sample token.</param>
    /// <param name="parameterName">The message parameter name used in validation errors.</param>
    /// <exception cref="ArgumentException">Thrown when either identifier is zero.</exception>
    private static void ValidateCaptureIntent(ulong correlationId, uint intentId, string parameterName)
    {
        if (correlationId == 0 || intentId == 0)
        {
            throw new ArgumentException("Capture intents require nonzero correlation and intent identifiers.", parameterName);
        }
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

        int challengeOffset = 17 + tokenLength;
        int ownerLifetimeIdOffset = challengeOffset + Constants.IpcChallengeBytes;
        if (payload.Length != ownerLifetimeIdOffset + Constants.IpcOwnerLifetimeIdBytes)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        var instanceId = new AdapterInstanceId(new Guid(payload[..16], bigEndian: true));
        byte[] token = payload.Slice(17, tokenLength).ToArray();
        byte[] challenge = payload.Slice(challengeOffset, Constants.IpcChallengeBytes).ToArray();
        byte[] ownerLifetimeId = payload.Slice(ownerLifetimeIdOffset, Constants.IpcOwnerLifetimeIdBytes).ToArray();
        return IpcDecodeResult.Success(new IpcHelloMessage(correlationId, instanceId, token, challenge, ownerLifetimeId));
    }

    /// <summary>Decodes an <see cref="IpcHelloAckMessage"/> payload, validating its boolean and enum fields.</summary>
    private static IpcDecodeResult DecodeHelloAck(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 2 + Constants.IpcHostProofBytes || payload[0] > 1 || !Enum.IsDefined((IpcHelloRejectReason)payload[1]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        bool accepted = payload[0] == 1;
        bool hasRejectReason = (IpcHelloRejectReason)payload[1] != IpcHelloRejectReason.None;
        if (accepted == hasRejectReason)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        byte[] hostProof = payload.Slice(2, Constants.IpcHostProofBytes).ToArray();
        return IpcDecodeResult.Success(
            new IpcHelloAckMessage(correlationId, accepted, (IpcHelloRejectReason)payload[1], hostProof));
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

    /// <summary>Decodes a host-directed event-listening request.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The fixed four-byte event key payload.</param>
    private static IpcDecodeResult DecodeListenEvent(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0 || payload.Length != sizeof(uint))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        uint eventKey = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        return eventKey == 0
            ? IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload)
            : IpcDecodeResult.Success(new IpcListenEventMessage(correlationId, eventKey));
    }

    /// <summary>Decodes a host-directed sample-read request.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The fixed four-byte sample token payload.</param>
    private static IpcDecodeResult DecodeReadSample(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0 || payload.Length != sizeof(uint))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        uint sampleToken = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        return sampleToken == 0
            ? IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload)
            : IpcDecodeResult.Success(new IpcReadSampleMessage(correlationId, sampleToken));
    }

    /// <summary>Encodes a pairing-display request: one mode byte followed by the fixed-length code digits.</summary>
    /// <param name="pairingDisplay">The display request to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the correlation id, mode, or code is invalid.</exception>
    private static byte[] EncodePairingDisplay(IpcPairingDisplayMessage pairingDisplay)
    {
        if (pairingDisplay.CorrelationId == 0)
        {
            throw new ArgumentException("A pairing-display request must have a nonzero correlation id.", nameof(pairingDisplay));
        }

        if (!Enum.IsDefined(pairingDisplay.Mode))
        {
            throw new ArgumentException("The pairing-display mode is not a recognized value.", nameof(pairingDisplay));
        }

        byte[] code = ValidatePairingCode(pairingDisplay.Code, nameof(pairingDisplay));
        var payload = new byte[1 + code.Length];
        payload[0] = (byte)pairingDisplay.Mode;
        code.CopyTo(payload.AsSpan(1));
        return payload;
    }

    /// <summary>Encodes a pairing-display acknowledgement after enforcing its required request correlation.</summary>
    /// <param name="pairingDisplayAck">The acknowledgement to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the acknowledgement has correlation id zero.</exception>
    private static byte[] EncodePairingDisplayAck(IpcPairingDisplayAckMessage pairingDisplayAck)
    {
        if (pairingDisplayAck.CorrelationId == 0)
        {
            throw new ArgumentException(
                "A pairing-display acknowledgement must identify a nonzero request correlation id.", nameof(pairingDisplayAck));
        }

        return [pairingDisplayAck.Accepted ? (byte)1 : (byte)0];
    }

    /// <summary>Encodes an attempts-exhausted notification after enforcing its unsolicited-message correlation rule.</summary>
    /// <param name="pairingAttemptsExhausted">The notification to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the notification carries a nonzero correlation id.</exception>
    private static byte[] EncodePairingAttemptsExhausted(IpcPairingAttemptsExhaustedMessage pairingAttemptsExhausted)
    {
        if (pairingAttemptsExhausted.CorrelationId != 0)
        {
            throw new ArgumentException("An attempts-exhausted notification must have correlation id zero.", nameof(pairingAttemptsExhausted));
        }

        return Array.Empty<byte>();
    }

    /// <summary>Validates a pairing code and converts it into its fixed-length ASCII-digit wire form.</summary>
    /// <param name="code">The code to validate.</param>
    /// <param name="parameterName">The message parameter name used in validation errors.</param>
    /// <exception cref="ArgumentException">Thrown when the code is not exactly the required count of ASCII decimal digits.</exception>
    private static byte[] ValidatePairingCode(string code, string parameterName)
    {
        if (code.Length != Constants.PairingChallengeCodeDigits)
        {
            throw new ArgumentException(
                $"A pairing code must be exactly {Constants.PairingChallengeCodeDigits} ASCII decimal digits.", parameterName);
        }

        var codeBytes = new byte[code.Length];
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c is < '0' or > '9')
            {
                throw new ArgumentException(
                    $"A pairing code must be exactly {Constants.PairingChallengeCodeDigits} ASCII decimal digits.", parameterName);
            }

            codeBytes[i] = (byte)c;
        }

        return codeBytes;
    }

    /// <summary>Decodes a pairing-display request, validating its mode and fixed-length code digits.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The one-byte mode followed by the fixed-length code payload.</param>
    private static IpcDecodeResult DecodePairingDisplay(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0
            || payload.Length != 1 + Constants.PairingChallengeCodeDigits
            || !Enum.IsDefined((PairingDisplayMode)payload[0]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        ReadOnlySpan<byte> codeBytes = payload[1..];
        Span<char> codeChars = stackalloc char[Constants.PairingChallengeCodeDigits];
        for (int i = 0; i < codeBytes.Length; i++)
        {
            if (codeBytes[i] is < (byte)'0' or > (byte)'9')
            {
                return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
            }

            codeChars[i] = (char)codeBytes[i];
        }

        return IpcDecodeResult.Success(new IpcPairingDisplayMessage(correlationId, new string(codeChars), (PairingDisplayMode)payload[0]));
    }

    /// <summary>Decodes a pairing-display acknowledgement, validating its boolean field.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The fixed one-byte accepted-flag payload.</param>
    private static IpcDecodeResult DecodePairingDisplayAck(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0 || payload.Length != 1 || payload[0] > 1)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcPairingDisplayAckMessage(correlationId, payload[0] == 1));
    }

    /// <summary>Decodes an attempts-exhausted notification, validating its unsolicited-message correlation rule.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The empty payload.</param>
    private static IpcDecodeResult DecodePairingAttemptsExhausted(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId != 0 || !payload.IsEmpty)
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcPairingAttemptsExhaustedMessage(correlationId));
    }

    /// <summary>Encodes a trust-admin request: one operation byte followed by its operation-specific argument bytes.</summary>
    /// <param name="trustAdminRequest">The request to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the correlation id, operation, or argument shape is invalid.</exception>
    private static byte[] EncodeTrustAdminRequest(IpcTrustAdminRequestMessage trustAdminRequest)
    {
        if (trustAdminRequest.CorrelationId == 0)
        {
            throw new ArgumentException("A trust-admin request must have a nonzero correlation id.", nameof(trustAdminRequest));
        }

        if (!Enum.IsDefined(trustAdminRequest.Operation))
        {
            throw new ArgumentException("The trust-admin operation is not a recognized value.", nameof(trustAdminRequest));
        }

        byte[] argument = trustAdminRequest.Operation switch
        {
            TrustAdminOperation.List => EncodeTrustAdminListArgument(trustAdminRequest),
            TrustAdminOperation.Revoke or TrustAdminOperation.Block or TrustAdminOperation.Unblock or TrustAdminOperation.Forget =>
                EncodeTrustAdminShortIdArgument(trustAdminRequest),
            TrustAdminOperation.ConfirmReset => EncodeTrustAdminConfirmationCodeArgument(trustAdminRequest),
            _ => EncodeTrustAdminNoArgument(trustAdminRequest),
        };

        var payload = new byte[1 + argument.Length];
        payload[0] = (byte)trustAdminRequest.Operation;
        argument.CopyTo(payload.AsSpan(1));
        return payload;
    }

    /// <summary>Validates and encodes the empty argument required by a no-argument trust-admin operation.</summary>
    /// <param name="trustAdminRequest">The request being encoded.</param>
    /// <exception cref="ArgumentException">Thrown when any argument field is set.</exception>
    private static byte[] EncodeTrustAdminNoArgument(IpcTrustAdminRequestMessage trustAdminRequest)
    {
        if (trustAdminRequest.ListScope is not null || trustAdminRequest.ShortId is not null || trustAdminRequest.ConfirmationCode is not null)
        {
            throw new ArgumentException($"{trustAdminRequest.Operation} takes no argument.", nameof(trustAdminRequest));
        }

        return Array.Empty<byte>();
    }

    /// <summary>Validates and encodes the scope argument required by <see cref="TrustAdminOperation.List"/>.</summary>
    /// <param name="trustAdminRequest">The request being encoded.</param>
    /// <exception cref="ArgumentException">Thrown when the scope is missing, an unrelated argument is set, or the scope is not a recognized value.</exception>
    private static byte[] EncodeTrustAdminListArgument(IpcTrustAdminRequestMessage trustAdminRequest)
    {
        if (trustAdminRequest.ListScope is null || trustAdminRequest.ShortId is not null || trustAdminRequest.ConfirmationCode is not null)
        {
            throw new ArgumentException("List requires exactly a scope argument.", nameof(trustAdminRequest));
        }

        if (!Enum.IsDefined(trustAdminRequest.ListScope.Value))
        {
            throw new ArgumentException("The list scope is not a recognized value.", nameof(trustAdminRequest));
        }

        return [(byte)trustAdminRequest.ListScope.Value];
    }

    /// <summary>Validates and encodes the short-id argument required by the device-targeted trust-admin operations.</summary>
    /// <param name="trustAdminRequest">The request being encoded.</param>
    /// <exception cref="ArgumentException">Thrown when the short id is missing, an unrelated argument is set, or the short id is not the required digit shape.</exception>
    private static byte[] EncodeTrustAdminShortIdArgument(IpcTrustAdminRequestMessage trustAdminRequest)
    {
        if (trustAdminRequest.ShortId is null || trustAdminRequest.ListScope is not null || trustAdminRequest.ConfirmationCode is not null)
        {
            throw new ArgumentException($"{trustAdminRequest.Operation} requires exactly a short id argument.", nameof(trustAdminRequest));
        }

        return ValidateFixedDigits(trustAdminRequest.ShortId, Constants.PairingShortIdDigits, nameof(trustAdminRequest));
    }

    /// <summary>Validates and encodes the confirmation-code argument required by <see cref="TrustAdminOperation.ConfirmReset"/>.</summary>
    /// <param name="trustAdminRequest">The request being encoded.</param>
    /// <exception cref="ArgumentException">Thrown when the code is missing, an unrelated argument is set, or the code is not the required digit shape.</exception>
    private static byte[] EncodeTrustAdminConfirmationCodeArgument(IpcTrustAdminRequestMessage trustAdminRequest)
    {
        if (trustAdminRequest.ConfirmationCode is null || trustAdminRequest.ListScope is not null || trustAdminRequest.ShortId is not null)
        {
            throw new ArgumentException("ConfirmReset requires exactly a confirmation code argument.", nameof(trustAdminRequest));
        }

        return ValidateFixedDigits(trustAdminRequest.ConfirmationCode, Constants.FactoryResetChallengeCodeDigits, nameof(trustAdminRequest));
    }

    /// <summary>Validates a value is exactly the required count of ASCII decimal digits and converts it to wire bytes.</summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="expectedLength">The exact required digit count.</param>
    /// <param name="parameterName">The message parameter name used in validation errors.</param>
    /// <exception cref="ArgumentException">Thrown when the value is not exactly the required count of ASCII decimal digits.</exception>
    private static byte[] ValidateFixedDigits(string value, int expectedLength, string parameterName)
    {
        if (value.Length != expectedLength)
        {
            throw new ArgumentException($"Expected exactly {expectedLength} ASCII decimal digits.", parameterName);
        }

        var bytes = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c is < '0' or > '9')
            {
                throw new ArgumentException($"Expected exactly {expectedLength} ASCII decimal digits.", parameterName);
            }

            bytes[i] = (byte)c;
        }

        return bytes;
    }

    /// <summary>Encodes a trust-admin result after bounding its UTF-8 encoded length.</summary>
    /// <param name="trustAdminResult">The result to encode.</param>
    /// <exception cref="ArgumentException">Thrown when the correlation id is zero or the result text exceeds the configured bound.</exception>
    private static byte[] EncodeTrustAdminResult(IpcTrustAdminResultMessage trustAdminResult)
    {
        if (trustAdminResult.CorrelationId == 0)
        {
            throw new ArgumentException("A trust-admin result must have a nonzero correlation id.", nameof(trustAdminResult));
        }

        byte[] resultTextBytes = Encoding.UTF8.GetBytes(trustAdminResult.ResultText);
        if (resultTextBytes.Length > Constants.MaxIpcTrustAdminResultTextBytes)
        {
            throw new ArgumentException(
                $"The trust-admin result text must be at most {Constants.MaxIpcTrustAdminResultTextBytes} UTF-8 bytes.", nameof(trustAdminResult));
        }

        return resultTextBytes;
    }

    /// <summary>Decodes a trust-admin request, validating the operation and its exact operation-specific argument shape.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The one-byte operation followed by its operation-specific argument payload.</param>
    private static IpcDecodeResult DecodeTrustAdminRequest(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0 || payload.IsEmpty || !Enum.IsDefined((TrustAdminOperation)payload[0]))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        var operation = (TrustAdminOperation)payload[0];
        ReadOnlySpan<byte> argument = payload[1..];

        switch (operation)
        {
            case TrustAdminOperation.Help:
            case TrustAdminOperation.ResetTrust:
            case TrustAdminOperation.Reset:
                return argument.IsEmpty
                    ? IpcDecodeResult.Success(new IpcTrustAdminRequestMessage(correlationId, operation))
                    : IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);

            case TrustAdminOperation.List:
                if (argument.Length != 1 || !Enum.IsDefined((TrustAdminListScope)argument[0]))
                {
                    return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
                }

                return IpcDecodeResult.Success(new IpcTrustAdminRequestMessage(correlationId, operation, ListScope: (TrustAdminListScope)argument[0]));

            case TrustAdminOperation.Revoke:
            case TrustAdminOperation.Block:
            case TrustAdminOperation.Unblock:
            case TrustAdminOperation.Forget:
                return TryDecodeFixedDigits(argument, Constants.PairingShortIdDigits, out string? shortId)
                    ? IpcDecodeResult.Success(new IpcTrustAdminRequestMessage(correlationId, operation, ShortId: shortId))
                    : IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);

            case TrustAdminOperation.ConfirmReset:
                return TryDecodeFixedDigits(argument, Constants.FactoryResetChallengeCodeDigits, out string? confirmationCode)
                    ? IpcDecodeResult.Success(new IpcTrustAdminRequestMessage(correlationId, operation, ConfirmationCode: confirmationCode))
                    : IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);

            default:
                return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }
    }

    /// <summary>Attempts to decode a fixed-length run of ASCII decimal digits into a string.</summary>
    /// <param name="bytes">The candidate digit bytes.</param>
    /// <param name="expectedLength">The exact required digit count.</param>
    /// <param name="value">The decoded digits when this returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    private static bool TryDecodeFixedDigits(ReadOnlySpan<byte> bytes, int expectedLength, out string? value)
    {
        if (bytes.Length != expectedLength)
        {
            value = null;
            return false;
        }

        Span<char> chars = stackalloc char[expectedLength];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is < (byte)'0' or > (byte)'9')
            {
                value = null;
                return false;
            }

            chars[i] = (char)bytes[i];
        }

        value = new string(chars);
        return true;
    }

    /// <summary>Decodes a trust-admin result, validating its bounded, well-formed UTF-8 result text.</summary>
    /// <param name="correlationId">The request correlation id from the frame header.</param>
    /// <param name="payload">The UTF-8 encoded result text.</param>
    private static IpcDecodeResult DecodeTrustAdminResult(ulong correlationId, ReadOnlySpan<byte> payload)
    {
        if (correlationId == 0 || payload.Length > Constants.MaxIpcTrustAdminResultTextBytes || !Utf8.IsValid(payload))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedPayload);
        }

        return IpcDecodeResult.Success(new IpcTrustAdminResultMessage(correlationId, Encoding.UTF8.GetString(payload)));
    }
}
