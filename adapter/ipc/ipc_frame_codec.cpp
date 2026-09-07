#include "ipc/ipc_frame_codec.hpp"

#include "ipc/ipc_constants.hpp"

#include <algorithm>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <type_traits>
#include <utility>
#include <variant>

namespace dovahlink::adapter::ipc {

namespace {

///  Writes a 32-bit value into exactly 4 bytes, least-significant byte first.
void WriteUInt32LittleEndian(std::span<std::byte, 4> destination,
                             std::uint32_t value) {
  for (int index = 0; index < 4; ++index) {
    destination[static_cast<std::size_t>(index)] =
        static_cast<std::byte>((value >> (8 * index)) & 0xFF);
  }
}

///  Reads exactly 4 bytes as a 32-bit value, least-significant byte first.
std::uint32_t ReadUInt32LittleEndian(std::span<const std::byte, 4> source) {
  std::uint32_t value = 0;
  for (int index = 0; index < 4; ++index) {
    value |= static_cast<std::uint32_t>(std::to_integer<std::uint8_t>(
                 source[static_cast<std::size_t>(index)]))
             << (8 * index);
  }
  return value;
}

///  Writes a 64-bit value into exactly 8 bytes, least-significant byte first.
void WriteUInt64LittleEndian(std::span<std::byte, 8> destination,
                             std::uint64_t value) {
  for (int index = 0; index < 8; ++index) {
    destination[static_cast<std::size_t>(index)] =
        static_cast<std::byte>((value >> (8 * index)) & 0xFF);
  }
}

///  Reads exactly 8 bytes as a 64-bit value, least-significant byte first.
std::uint64_t ReadUInt64LittleEndian(std::span<const std::byte, 8> source) {
  std::uint64_t value = 0;
  for (int index = 0; index < 8; ++index) {
    value |= static_cast<std::uint64_t>(std::to_integer<std::uint8_t>(
                 source[static_cast<std::size_t>(index)]))
             << (8 * index);
  }
  return value;
}

///  Whether `value` is one of `IpcMessageKind`'s contiguous defined values.
constexpr bool IsDefinedMessageKind(std::uint8_t value) {
  return value >= 1 && value <= 14;
}

///  Whether `value` is one of `TrustAdminOperation`'s contiguous defined
///  values.
constexpr bool IsDefinedTrustAdminOperation(std::uint8_t value) {
  return value <= 8;
}

///  Whether `value` is one of `TrustAdminListScope`'s contiguous defined
///  values.
constexpr bool IsDefinedTrustAdminListScope(std::uint8_t value) {
  return value <= 2;
}

///  Decodes a fixed-length run of ASCII decimal digit bytes into a string, or
///  `std::nullopt` if `bytes` is not exactly `expectedLength` ASCII decimal
///  digits.
std::optional<std::string>
TryDecodeFixedDigits(std::span<const std::byte> bytes,
                     std::size_t expectedLength) {
  if (bytes.size() != expectedLength) {
    return std::nullopt;
  }

  std::string value(expectedLength, '\0');
  for (std::size_t index = 0; index < expectedLength; ++index) {
    const auto digitByte = std::to_integer<std::uint8_t>(bytes[index]);
    if (digitByte < static_cast<std::uint8_t>('0') ||
        digitByte > static_cast<std::uint8_t>('9')) {
      return std::nullopt;
    }
    value[index] = static_cast<char>(digitByte);
  }

  return value;
}

///  Whether `bytes` is well-formed UTF-8: every multi-byte sequence has the
///  correct number of continuation bytes, encodes no surrogate or
///  out-of-range codepoint, and is not an overlong encoding of a codepoint
///  that a shorter sequence could already represent.
constexpr bool IsValidUtf8(std::span<const std::byte> bytes) {
  std::size_t index = 0;
  while (index < bytes.size()) {
    const auto lead = std::to_integer<std::uint8_t>(bytes[index]);
    std::size_t continuationCount;
    std::uint32_t minCodepoint;
    std::uint32_t codepoint;
    if (lead <= 0x7F) {
      ++index;
      continue;
    } else if ((lead & 0xE0) == 0xC0) {
      continuationCount = 1;
      minCodepoint = 0x80;
      codepoint = lead & 0x1F;
    } else if ((lead & 0xF0) == 0xE0) {
      continuationCount = 2;
      minCodepoint = 0x800;
      codepoint = lead & 0x0F;
    } else if ((lead & 0xF8) == 0xF0) {
      continuationCount = 3;
      minCodepoint = 0x10000;
      codepoint = lead & 0x07;
    } else {
      return false;
    }

    if (index + continuationCount + 1 > bytes.size()) {
      return false;
    }

    for (std::size_t offset = 1; offset <= continuationCount; ++offset) {
      const auto continuationByte =
          std::to_integer<std::uint8_t>(bytes[index + offset]);
      if ((continuationByte & 0xC0) != 0x80) {
        return false;
      }
      codepoint = (codepoint << 6) | (continuationByte & 0x3F);
    }

    if (codepoint < minCodepoint || codepoint > 0x10FFFF ||
        (codepoint >= 0xD800 && codepoint <= 0xDFFF)) {
      return false;
    }

    index += continuationCount + 1;
  }

  return true;
}

///  Whether `value` is one of `PairingDisplayMode`'s contiguous defined
///  values.
constexpr bool IsDefinedPairingDisplayMode(std::uint8_t value) {
  return value <= 2;
}

///  Whether every character in `text` is an ASCII decimal digit.
constexpr bool AreAllAsciiDigits(std::string_view text) {
  return std::ranges::all_of(text, [](char c) { return c >= '0' && c <= '9'; });
}

///  Whether every character in `text` is an ASCII decimal digit and `text` is
///  exactly `expectedLength` characters long.
constexpr bool IsFixedAsciiDigits(std::string_view text,
                                  std::size_t expectedLength) {
  return text.size() == expectedLength && AreAllAsciiDigits(text);
}

///  Whether `value` is one of `IpcCloseReason`'s contiguous defined values.
constexpr bool IsDefinedCloseReason(std::uint8_t value) { return value <= 2; }

///  Whether `value` is one of `IpcRejectReason`'s contiguous defined values.
constexpr bool IsDefinedRejectReason(std::uint8_t value) { return value <= 5; }

///  Whether `value` is one of `IpcHelloRejectReason`'s contiguous defined
///  values.
constexpr bool IsDefinedHelloRejectReason(std::uint8_t value) {
  return value <= 3;
}

} //  namespace

std::vector<std::byte>
IpcFrameCodec::EncodeHello(const IpcHelloMessage &hello) {
  if (hello.peerProofToken.size() > kMaxIpcPeerProofTokenBytes) {
    throw std::invalid_argument(
        "The peer-proof token exceeds kMaxIpcPeerProofTokenBytes.");
  }

  const std::size_t tokenOffset = 17;
  const std::size_t challengeOffset = tokenOffset + hello.peerProofToken.size();
  const std::size_t ownerLifetimeIdOffset =
      challengeOffset + kIpcChallengeBytes;
  std::vector<std::byte> payload(ownerLifetimeIdOffset +
                                 kIpcOwnerLifetimeIdBytes);
  std::ranges::copy(hello.adapterInstanceId, payload.begin());
  payload[16] = static_cast<std::byte>(hello.peerProofToken.size());
  std::ranges::copy(hello.peerProofToken,
                    payload.begin() + static_cast<std::ptrdiff_t>(tokenOffset));
  std::ranges::copy(hello.challenge,
                    payload.begin() +
                        static_cast<std::ptrdiff_t>(challengeOffset));
  std::ranges::copy(hello.ownerLifetimeId,
                    payload.begin() +
                        static_cast<std::ptrdiff_t>(ownerLifetimeIdOffset));
  return payload;
}

std::vector<std::byte>
IpcFrameCodec::EncodeHelloAck(const IpcHelloAckMessage &helloAck) {
  const bool hasRejectReason =
      helloAck.rejectReason != IpcHelloRejectReason::kNone;
  if (helloAck.accepted == hasRejectReason) {
    throw std::invalid_argument(
        "An accepted HelloAck must have no reject reason, and a rejected "
        "HelloAck must have one.");
  }

  std::vector<std::byte> payload(2 + kIpcHostProofBytes);
  payload[0] = static_cast<std::byte>(helloAck.accepted ? 1 : 0);
  payload[1] =
      static_cast<std::byte>(std::to_underlying(helloAck.rejectReason));
  std::ranges::copy(helloAck.hostProof, payload.begin() + 2);
  return payload;
}

std::vector<std::byte>
IpcFrameCodec::EncodeListenEvent(const IpcListenEventMessage &listenEvent) {
  if (listenEvent.correlationId == 0 || listenEvent.eventKey == 0) {
    throw std::invalid_argument(
        "Capture intents require nonzero correlation and intent identifiers.");
  }

  std::vector<std::byte> payload(sizeof(std::uint32_t));
  WriteUInt32LittleEndian(
      std::span<std::byte, 4>(payload.data(), sizeof(std::uint32_t)),
      listenEvent.eventKey);
  return payload;
}

std::vector<std::byte>
IpcFrameCodec::EncodeReadSample(const IpcReadSampleMessage &readSample) {
  if (readSample.correlationId == 0 || readSample.sampleToken == 0) {
    throw std::invalid_argument(
        "Capture intents require nonzero correlation and intent identifiers.");
  }

  std::vector<std::byte> payload(sizeof(std::uint32_t));
  WriteUInt32LittleEndian(
      std::span<std::byte, 4>(payload.data(), sizeof(std::uint32_t)),
      readSample.sampleToken);
  return payload;
}

std::vector<std::byte> IpcFrameCodec::EncodePairingDisplay(
    const IpcPairingDisplayMessage &pairingDisplay) {
  if (pairingDisplay.correlationId == 0) {
    throw std::invalid_argument(
        "A pairing-display request must have a nonzero correlation id.");
  }

  if (!IsDefinedPairingDisplayMode(std::to_underlying(pairingDisplay.mode))) {
    throw std::invalid_argument(
        "The pairing-display mode is not a recognized value.");
  }

  if (pairingDisplay.code.size() != kPairingChallengeCodeDigits ||
      !AreAllAsciiDigits(pairingDisplay.code)) {
    throw std::invalid_argument(
        "A pairing code must be exactly kPairingChallengeCodeDigits ASCII "
        "decimal digits.");
  }

  std::vector<std::byte> payload(1 + kPairingChallengeCodeDigits);
  payload[0] = static_cast<std::byte>(std::to_underlying(pairingDisplay.mode));
  for (std::size_t index = 0; index < pairingDisplay.code.size(); ++index) {
    payload[1 + index] = static_cast<std::byte>(
        static_cast<unsigned char>(pairingDisplay.code[index]));
  }

  return payload;
}

std::vector<std::byte> IpcFrameCodec::EncodeTrustAdminRequest(
    const IpcTrustAdminRequestMessage &trustAdminRequest) {
  if (trustAdminRequest.correlationId == 0) {
    throw std::invalid_argument(
        "A trust-admin request must have a nonzero correlation id.");
  }

  if (!IsDefinedTrustAdminOperation(
          std::to_underlying(trustAdminRequest.operation))) {
    throw std::invalid_argument(
        "The trust-admin operation is not a recognized value.");
  }

  std::vector<std::byte> argument;
  switch (trustAdminRequest.operation) {
  case TrustAdminOperation::kHelp:
  case TrustAdminOperation::kResetTrust:
  case TrustAdminOperation::kReset:
    if (trustAdminRequest.listScope.has_value() ||
        trustAdminRequest.shortId.has_value() ||
        trustAdminRequest.confirmationCode.has_value()) {
      throw std::invalid_argument("This operation takes no argument.");
    }
    break;

  case TrustAdminOperation::kList:
    if (!trustAdminRequest.listScope.has_value() ||
        trustAdminRequest.shortId.has_value() ||
        trustAdminRequest.confirmationCode.has_value()) {
      throw std::invalid_argument("List requires exactly a scope argument.");
    }
    if (!IsDefinedTrustAdminListScope(
            std::to_underlying(*trustAdminRequest.listScope))) {
      throw std::invalid_argument("The list scope is not a recognized value.");
    }
    argument = {static_cast<std::byte>(
        std::to_underlying(*trustAdminRequest.listScope))};
    break;

  case TrustAdminOperation::kRevoke:
  case TrustAdminOperation::kBlock:
  case TrustAdminOperation::kUnblock:
  case TrustAdminOperation::kForget:
    if (!trustAdminRequest.shortId.has_value() ||
        trustAdminRequest.listScope.has_value() ||
        trustAdminRequest.confirmationCode.has_value() ||
        !IsFixedAsciiDigits(*trustAdminRequest.shortId,
                            kPairingShortIdDigits)) {
      throw std::invalid_argument(
          "This operation requires exactly a valid short id argument.");
    }
    argument.resize(trustAdminRequest.shortId->size());
    std::ranges::transform(
        *trustAdminRequest.shortId, argument.begin(), [](char c) {
          return static_cast<std::byte>(static_cast<unsigned char>(c));
        });
    break;

  case TrustAdminOperation::kConfirmReset:
    if (!trustAdminRequest.confirmationCode.has_value() ||
        trustAdminRequest.listScope.has_value() ||
        trustAdminRequest.shortId.has_value() ||
        !IsFixedAsciiDigits(*trustAdminRequest.confirmationCode,
                            kFactoryResetChallengeCodeDigits)) {
      throw std::invalid_argument(
          "ConfirmReset requires exactly a valid confirmation code argument.");
    }
    argument.resize(trustAdminRequest.confirmationCode->size());
    std::ranges::transform(
        *trustAdminRequest.confirmationCode, argument.begin(), [](char c) {
          return static_cast<std::byte>(static_cast<unsigned char>(c));
        });
    break;
  }

  std::vector<std::byte> payload(1 + argument.size());
  payload[0] =
      static_cast<std::byte>(std::to_underlying(trustAdminRequest.operation));
  std::ranges::copy(argument, payload.begin() + 1);
  return payload;
}

std::vector<std::byte> IpcFrameCodec::EncodeTrustAdminResult(
    const IpcTrustAdminResultMessage &trustAdminResult) {
  if (trustAdminResult.correlationId == 0) {
    throw std::invalid_argument(
        "A trust-admin result must have a nonzero correlation id.");
  }

  if (trustAdminResult.resultText.size() > kMaxIpcTrustAdminResultTextBytes) {
    throw std::invalid_argument("The trust-admin result text exceeds "
                                "kMaxIpcTrustAdminResultTextBytes.");
  }

  std::vector<std::byte> payload(trustAdminResult.resultText.size());
  std::ranges::transform(
      trustAdminResult.resultText, payload.begin(), [](char c) {
        return static_cast<std::byte>(static_cast<unsigned char>(c));
      });
  return payload;
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeTrustAdminRequest(std::uint64_t correlationId,
                                       std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.empty()) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto operationByte = std::to_integer<std::uint8_t>(payload[0]);
  if (!IsDefinedTrustAdminOperation(operationByte)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto operation = static_cast<TrustAdminOperation>(operationByte);
  const std::span<const std::byte> argument = payload.subspan(1);

  IpcTrustAdminRequestMessage request{.correlationId = correlationId,
                                      .operation = operation};

  switch (operation) {
  case TrustAdminOperation::kHelp:
  case TrustAdminOperation::kResetTrust:
  case TrustAdminOperation::kReset:
    if (!argument.empty()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    return IpcMessage{request};

  case TrustAdminOperation::kList: {
    if (argument.size() != 1) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    const auto scopeByte = std::to_integer<std::uint8_t>(argument[0]);
    if (!IsDefinedTrustAdminListScope(scopeByte)) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    request.listScope = static_cast<TrustAdminListScope>(scopeByte);
    return IpcMessage{request};
  }

  case TrustAdminOperation::kRevoke:
  case TrustAdminOperation::kBlock:
  case TrustAdminOperation::kUnblock:
  case TrustAdminOperation::kForget: {
    std::optional<std::string> shortId =
        TryDecodeFixedDigits(argument, kPairingShortIdDigits);
    if (!shortId.has_value()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    request.shortId = std::move(shortId);
    return IpcMessage{request};
  }

  case TrustAdminOperation::kConfirmReset: {
    std::optional<std::string> confirmationCode =
        TryDecodeFixedDigits(argument, kFactoryResetChallengeCodeDigits);
    if (!confirmationCode.has_value()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    request.confirmationCode = std::move(confirmationCode);
    return IpcMessage{request};
  }
  }

  return std::unexpected(IpcRejectReason::kMalformedPayload);
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeTrustAdminResult(std::uint64_t correlationId,
                                      std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.size() > kMaxIpcTrustAdminResultTextBytes ||
      !IsValidUtf8(payload)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  std::string resultText(payload.size(), '\0');
  std::ranges::transform(payload, resultText.begin(), [](std::byte b) {
    return static_cast<char>(std::to_integer<unsigned char>(b));
  });

  return IpcMessage{IpcTrustAdminResultMessage{
      .correlationId = correlationId, .resultText = std::move(resultText)}};
}

std::vector<std::byte> IpcFrameCodec::Encode(const IpcMessage &message) const {
  IpcMessageKind kind{};
  std::uint64_t correlationId = 0;
  std::vector<std::byte> payload;

  std::visit(
      [&](const auto &value) {
        correlationId = value.correlationId;
        using T = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<T, IpcHelloMessage>) {
          kind = IpcMessageKind::kHello;
          payload = EncodeHello(value);
        } else if constexpr (std::is_same_v<T, IpcHelloAckMessage>) {
          kind = IpcMessageKind::kHelloAck;
          payload = EncodeHelloAck(value);
        } else if constexpr (std::is_same_v<T,
                                            IpcResynchronizeRequestMessage>) {
          kind = IpcMessageKind::kResynchronizeRequest;
        } else if constexpr (std::is_same_v<T, IpcResynchronizeResultMessage>) {
          kind = IpcMessageKind::kResynchronizeResult;
          payload = {static_cast<std::byte>(value.accepted ? 1 : 0)};
        } else if constexpr (std::is_same_v<T, IpcCloseMessage>) {
          if (value.correlationId != 0) {
            throw std::invalid_argument(
                "A close message must have correlation id zero.");
          }
          kind = IpcMessageKind::kClose;
          payload = {static_cast<std::byte>(std::to_underlying(value.reason))};
        } else if constexpr (std::is_same_v<T, IpcRejectMessage>) {
          kind = IpcMessageKind::kReject;
          payload = {static_cast<std::byte>(std::to_underlying(value.reason))};
        } else if constexpr (std::is_same_v<T, IpcCancelMessage>) {
          if (value.correlationId == 0) {
            throw std::invalid_argument("A cancel message must identify a "
                                        "nonzero request correlation id.");
          }
          kind = IpcMessageKind::kCancel;
        } else if constexpr (std::is_same_v<T, IpcListenEventMessage>) {
          kind = IpcMessageKind::kListenEvent;
          payload = EncodeListenEvent(value);
        } else if constexpr (std::is_same_v<T, IpcReadSampleMessage>) {
          kind = IpcMessageKind::kReadSample;
          payload = EncodeReadSample(value);
        } else if constexpr (std::is_same_v<T, IpcPairingDisplayMessage>) {
          kind = IpcMessageKind::kPairingDisplay;
          payload = EncodePairingDisplay(value);
        } else if constexpr (std::is_same_v<T, IpcPairingDisplayAckMessage>) {
          if (value.correlationId == 0) {
            throw std::invalid_argument(
                "A pairing-display acknowledgement must identify a nonzero "
                "request correlation id.");
          }
          kind = IpcMessageKind::kPairingDisplayAck;
          payload = {static_cast<std::byte>(value.accepted ? 1 : 0)};
        } else if constexpr (std::is_same_v<
                                 T, IpcPairingAttemptsExhaustedMessage>) {
          if (value.correlationId != 0) {
            throw std::invalid_argument(
                "An attempts-exhausted notification must have correlation id "
                "zero.");
          }
          kind = IpcMessageKind::kPairingAttemptsExhausted;
        } else if constexpr (std::is_same_v<T, IpcTrustAdminRequestMessage>) {
          kind = IpcMessageKind::kTrustAdminRequest;
          payload = EncodeTrustAdminRequest(value);
        } else if constexpr (std::is_same_v<T, IpcTrustAdminResultMessage>) {
          kind = IpcMessageKind::kTrustAdminResult;
          payload = EncodeTrustAdminResult(value);
        }
      },
      message);

  const auto totalLength =
      static_cast<std::uint32_t>(kIpcFrameHeaderBytes + payload.size());
  std::vector<std::byte> frame(sizeof(std::uint32_t) + totalLength);
  WriteUInt32LittleEndian(std::span<std::byte, 4>(frame.data(), 4),
                          totalLength);
  frame[4] = static_cast<std::byte>(std::to_underlying(kind));
  WriteUInt64LittleEndian(std::span<std::byte, 8>(frame.data() + 5, 8),
                          correlationId);
  std::ranges::copy(payload, frame.begin() + static_cast<std::ptrdiff_t>(
                                                 kIpcFrameHeaderBytes +
                                                 sizeof(std::uint32_t)));
  return frame;
}

std::optional<std::size_t> IpcFrameCodec::TryReadFrameLength(
    std::span<const std::byte> lengthPrefix) const {
  if (lengthPrefix.size() != sizeof(std::uint32_t)) {
    return std::nullopt;
  }

  const std::uint32_t declaredLength = ReadUInt32LittleEndian(
      std::span<const std::byte, 4>(lengthPrefix.data(), 4));
  if (declaredLength < kIpcFrameHeaderBytes ||
      declaredLength > kMaxIpcFrameBytes) {
    return std::nullopt;
  }

  return static_cast<std::size_t>(declaredLength);
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeHello(std::uint64_t correlationId,
                           std::span<const std::byte> payload) {
  if (payload.size() < 17) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto tokenLength = std::to_integer<std::size_t>(payload[16]);
  if (tokenLength > kMaxIpcPeerProofTokenBytes) {
    return std::unexpected(IpcRejectReason::kInvalidIdentity);
  }

  const std::size_t challengeOffset = 17 + tokenLength;
  const std::size_t ownerLifetimeIdOffset =
      challengeOffset + kIpcChallengeBytes;
  if (payload.size() != ownerLifetimeIdOffset + kIpcOwnerLifetimeIdBytes) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  IpcHelloMessage hello{.correlationId = correlationId};
  std::ranges::copy(payload.first(16), hello.adapterInstanceId.begin());
  hello.peerProofToken.assign(payload.begin() + 17,
                              payload.begin() +
                                  static_cast<std::ptrdiff_t>(challengeOffset));
  std::ranges::copy(payload.subspan(challengeOffset, kIpcChallengeBytes),
                    hello.challenge.begin());
  std::ranges::copy(
      payload.subspan(ownerLifetimeIdOffset, kIpcOwnerLifetimeIdBytes),
      hello.ownerLifetimeId.begin());
  return IpcMessage{hello};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeHelloAck(std::uint64_t correlationId,
                              std::span<const std::byte> payload) {
  if (payload.size() != 2 + kIpcHostProofBytes) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto acceptedByte = std::to_integer<std::uint8_t>(payload[0]);
  const auto rejectReasonByte = std::to_integer<std::uint8_t>(payload[1]);
  if (acceptedByte > 1 || !IsDefinedHelloRejectReason(rejectReasonByte)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const bool accepted = acceptedByte == 1;
  const bool hasRejectReason =
      static_cast<IpcHelloRejectReason>(rejectReasonByte) !=
      IpcHelloRejectReason::kNone;
  if (accepted == hasRejectReason) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  IpcHelloAckMessage helloAck{
      .correlationId = correlationId,
      .accepted = accepted,
      .rejectReason = static_cast<IpcHelloRejectReason>(rejectReasonByte),
  };
  std::ranges::copy(payload.subspan(2, kIpcHostProofBytes),
                    helloAck.hostProof.begin());
  return IpcMessage{helloAck};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeResynchronizeResult(std::uint64_t correlationId,
                                         std::span<const std::byte> payload) {
  if (payload.size() != 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto acceptedByte = std::to_integer<std::uint8_t>(payload[0]);
  if (acceptedByte > 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{IpcResynchronizeResultMessage{
      .correlationId = correlationId, .accepted = acceptedByte == 1}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeClose(std::uint64_t correlationId,
                           std::span<const std::byte> payload) {
  if (correlationId != 0 || payload.size() != 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto reasonByte = std::to_integer<std::uint8_t>(payload[0]);
  if (!IsDefinedCloseReason(reasonByte)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{
      IpcCloseMessage{.correlationId = correlationId,
                      .reason = static_cast<IpcCloseReason>(reasonByte)}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeReject(std::uint64_t correlationId,
                            std::span<const std::byte> payload) {
  if (payload.size() != 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto reasonByte = std::to_integer<std::uint8_t>(payload[0]);
  if (!IsDefinedRejectReason(reasonByte)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{
      IpcRejectMessage{.correlationId = correlationId,
                       .reason = static_cast<IpcRejectReason>(reasonByte)}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeListenEvent(std::uint64_t correlationId,
                                 std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.size() != sizeof(std::uint32_t)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const std::uint32_t eventKey = ReadUInt32LittleEndian(
      std::span<const std::byte, 4>(payload.data(), sizeof(std::uint32_t)));
  if (eventKey == 0) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{IpcListenEventMessage{.correlationId = correlationId,
                                          .eventKey = eventKey}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeReadSample(std::uint64_t correlationId,
                                std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.size() != sizeof(std::uint32_t)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const std::uint32_t sampleToken = ReadUInt32LittleEndian(
      std::span<const std::byte, 4>(payload.data(), sizeof(std::uint32_t)));
  if (sampleToken == 0) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{IpcReadSampleMessage{.correlationId = correlationId,
                                         .sampleToken = sampleToken}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodePairingDisplay(std::uint64_t correlationId,
                                    std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.size() != 1 + kPairingChallengeCodeDigits) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto modeByte = std::to_integer<std::uint8_t>(payload[0]);
  if (!IsDefinedPairingDisplayMode(modeByte)) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  std::string code(kPairingChallengeCodeDigits, '\0');
  for (std::size_t index = 0; index < kPairingChallengeCodeDigits; ++index) {
    const auto digitByte = std::to_integer<std::uint8_t>(payload[1 + index]);
    if (digitByte < static_cast<std::uint8_t>('0') ||
        digitByte > static_cast<std::uint8_t>('9')) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    code[index] = static_cast<char>(digitByte);
  }

  return IpcMessage{IpcPairingDisplayMessage{
      .correlationId = correlationId,
      .code = std::move(code),
      .mode = static_cast<PairingDisplayMode>(modeByte)}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodePairingDisplayAck(std::uint64_t correlationId,
                                       std::span<const std::byte> payload) {
  if (correlationId == 0 || payload.size() != 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  const auto acceptedByte = std::to_integer<std::uint8_t>(payload[0]);
  if (acceptedByte > 1) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  return IpcMessage{IpcPairingDisplayAckMessage{.correlationId = correlationId,
                                                .accepted = acceptedByte == 1}};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::Decode(std::span<const std::byte> frame) const {
  if (frame.size() < kIpcFrameHeaderBytes || frame.size() > kMaxIpcFrameBytes) {
    return std::unexpected(IpcRejectReason::kMalformedFrameLength);
  }

  const auto kindByte = std::to_integer<std::uint8_t>(frame[0]);
  if (!IsDefinedMessageKind(kindByte)) {
    return std::unexpected(IpcRejectReason::kUnknownMessageKind);
  }

  const std::uint64_t correlationId = ReadUInt64LittleEndian(
      std::span<const std::byte, 8>(frame.data() + 1, 8));
  const std::span<const std::byte> payload =
      frame.subspan(kIpcFrameHeaderBytes);

  switch (static_cast<IpcMessageKind>(kindByte)) {
  case IpcMessageKind::kHello:
    return DecodeHello(correlationId, payload);
  case IpcMessageKind::kHelloAck:
    return DecodeHelloAck(correlationId, payload);
  case IpcMessageKind::kResynchronizeRequest:
    if (!payload.empty()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    return IpcMessage{
        IpcResynchronizeRequestMessage{.correlationId = correlationId}};
  case IpcMessageKind::kResynchronizeResult:
    return DecodeResynchronizeResult(correlationId, payload);
  case IpcMessageKind::kClose:
    return DecodeClose(correlationId, payload);
  case IpcMessageKind::kReject:
    return DecodeReject(correlationId, payload);
  case IpcMessageKind::kCancel:
    if (correlationId == 0 || !payload.empty()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    return IpcMessage{IpcCancelMessage{.correlationId = correlationId}};
  case IpcMessageKind::kListenEvent:
    return DecodeListenEvent(correlationId, payload);
  case IpcMessageKind::kReadSample:
    return DecodeReadSample(correlationId, payload);
  case IpcMessageKind::kPairingDisplay:
    return DecodePairingDisplay(correlationId, payload);
  case IpcMessageKind::kPairingDisplayAck:
    return DecodePairingDisplayAck(correlationId, payload);
  case IpcMessageKind::kPairingAttemptsExhausted:
    if (correlationId != 0 || !payload.empty()) {
      return std::unexpected(IpcRejectReason::kMalformedPayload);
    }
    return IpcMessage{
        IpcPairingAttemptsExhaustedMessage{.correlationId = correlationId}};
  case IpcMessageKind::kTrustAdminRequest:
    return DecodeTrustAdminRequest(correlationId, payload);
  case IpcMessageKind::kTrustAdminResult:
    return DecodeTrustAdminResult(correlationId, payload);
  }

  return std::unexpected(IpcRejectReason::kUnknownMessageKind);
}

} //  namespace dovahlink::adapter::ipc
