#include "ipc/ipc_frame_codec.hpp"

#include "ipc/ipc_constants.hpp"

#include <algorithm>
#include <stdexcept>
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
  return value >= 1 && value <= 7;
}

///  Whether `value` is one of `IpcCloseReason`'s contiguous defined values.
constexpr bool IsDefinedCloseReason(std::uint8_t value) { return value <= 2; }

///  Whether `value` is one of `IpcRejectReason`'s contiguous defined values.
constexpr bool IsDefinedRejectReason(std::uint8_t value) { return value <= 4; }

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

  std::vector<std::byte> payload(17 + hello.peerProofToken.size());
  std::ranges::copy(hello.adapterInstanceId, payload.begin());
  payload[16] = static_cast<std::byte>(hello.peerProofToken.size());
  std::ranges::copy(hello.peerProofToken, payload.begin() + 17);
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

  return {
      static_cast<std::byte>(helloAck.accepted ? 1 : 0),
      static_cast<std::byte>(std::to_underlying(helloAck.rejectReason)),
  };
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

  if (payload.size() != 17 + tokenLength) {
    return std::unexpected(IpcRejectReason::kMalformedPayload);
  }

  IpcHelloMessage hello{.correlationId = correlationId};
  std::ranges::copy(payload.first(16), hello.adapterInstanceId.begin());
  hello.peerProofToken.assign(
      payload.begin() + 17,
      payload.begin() + static_cast<std::ptrdiff_t>(17 + tokenLength));
  return IpcMessage{hello};
}

std::expected<IpcMessage, IpcRejectReason>
IpcFrameCodec::DecodeHelloAck(std::uint64_t correlationId,
                              std::span<const std::byte> payload) {
  if (payload.size() != 2) {
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

  return IpcMessage{IpcHelloAckMessage{
      .correlationId = correlationId,
      .accepted = accepted,
      .rejectReason = static_cast<IpcHelloRejectReason>(rejectReasonByte),
  }};
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
  }

  return std::unexpected(IpcRejectReason::kUnknownMessageKind);
}

} //  namespace dovahlink::adapter::ipc
