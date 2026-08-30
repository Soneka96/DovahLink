#include "ipc/ipc_frame_codec.hpp"

#include "ipc/ipc_constants.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <expected>
#include <filesystem>
#include <fstream>
#include <limits>
#include <span>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <variant>
#include <vector>

using dovahlink::adapter::ipc::IIpcFrameCodec;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::IpcMessageKind;
using dovahlink::adapter::ipc::IpcRejectMessage;
using dovahlink::adapter::ipc::IpcRejectReason;
using dovahlink::adapter::ipc::IpcResynchronizeRequestMessage;
using dovahlink::adapter::ipc::IpcResynchronizeResultMessage;
using dovahlink::adapter::ipc::kIpcFrameHeaderBytes;
using dovahlink::adapter::ipc::kMaxIpcFrameBytes;
using dovahlink::adapter::ipc::kMaxIpcPeerProofTokenBytes;

namespace {

///  A representative, fixed adapter instance identity for tests that don't care
///  about its value.
std::array<std::byte, 16> SampleInstanceId() {
  std::array<std::byte, 16> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

///  Encodes a message, reads its declared length, and decodes the result,
///  mirroring real usage.
std::expected<IpcMessage, IpcRejectReason>
EncodeThenDecode(const IIpcFrameCodec &codec, const IpcMessage &message) {
  std::vector<std::byte> frameBytes = codec.Encode(message);
  auto frameLength = codec.TryReadFrameLength(std::span(frameBytes).first(4));
  REQUIRE(frameLength.has_value());
  REQUIRE(*frameLength == frameBytes.size() - 4);
  return codec.Decode(std::span(frameBytes).subspan(4, *frameLength));
}

///  Writes a 4-byte little-endian length prefix, for tests that build a prefix
///  directly.
std::array<std::byte, 4> LittleEndianLength(std::uint32_t value) {
  return {
      static_cast<std::byte>(value & 0xFF),
      static_cast<std::byte>((value >> 8) & 0xFF),
      static_cast<std::byte>((value >> 16) & 0xFF),
      static_cast<std::byte>((value >> 24) & 0xFF),
  };
}

///  Builds a frame's header-plus-payload bytes directly, for constructing wire
///  content the codec's own Encode cannot produce.
std::vector<std::byte> BuildFrame(IpcMessageKind kind,
                                  std::uint64_t correlationId,
                                  const std::vector<std::byte> &payload) {
  std::vector<std::byte> frame(kIpcFrameHeaderBytes + payload.size());
  frame[0] = static_cast<std::byte>(std::to_underlying(kind));
  for (int index = 0; index < 8; ++index) {
    frame[1 + static_cast<std::size_t>(index)] =
        static_cast<std::byte>((correlationId >> (8 * index)) & 0xFF);
  }
  std::ranges::copy(payload, frame.begin() + static_cast<std::ptrdiff_t>(
                                                 kIpcFrameHeaderBytes));
  return frame;
}

} //  namespace

//  ---- Round trips ----

TEST_CASE("hello with a token round-trips", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloMessage original{
      .correlationId = 7,
      .adapterInstanceId = SampleInstanceId(),
      .peerProofToken = {std::byte{1}, std::byte{2}, std::byte{3}}};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("decoded hello owns its token bytes", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloMessage original{
      .correlationId = 7,
      .adapterInstanceId = SampleInstanceId(),
      .peerProofToken = {std::byte{1}, std::byte{2}, std::byte{3}}};
  std::vector<std::byte> frameBytes = codec.Encode(IpcMessage{original});

  auto result = codec.Decode(std::span(frameBytes).subspan(4));

  REQUIRE(result.has_value());
  auto *decoded = std::get_if<IpcHelloMessage>(&*result);
  REQUIRE(decoded != nullptr);
  frameBytes.back() = std::byte{99};
  CHECK(decoded->peerProofToken == original.peerProofToken);
}

TEST_CASE("hello with an empty token round-trips", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloMessage original{.correlationId = 1,
                           .adapterInstanceId = SampleInstanceId(),
                           .peerProofToken = {}};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("hello with the maximum token length round-trips",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> token(kMaxIpcPeerProofTokenBytes);
  for (std::size_t index = 0; index < token.size(); ++index) {
    token[index] = static_cast<std::byte>(index);
  }
  IpcHelloMessage original{.correlationId = 1,
                           .adapterInstanceId = SampleInstanceId(),
                           .peerProofToken = token};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("accepted hello-ack round-trips", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloAckMessage original{.correlationId = 1,
                              .accepted = true,
                              .rejectReason = IpcHelloRejectReason::kNone};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("encoding an accepted hello-ack with a reject reason throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloAckMessage message{.correlationId = 1,
                             .accepted = true,
                             .rejectReason =
                                 IpcHelloRejectReason::kInvalidProof};

  CHECK_THROWS_AS(codec.Encode(IpcMessage{message}), std::invalid_argument);
}

TEST_CASE("encoding a rejected hello-ack without a reject reason throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloAckMessage message{.correlationId = 1,
                             .accepted = false,
                             .rejectReason = IpcHelloRejectReason::kNone};

  CHECK_THROWS_AS(codec.Encode(IpcMessage{message}), std::invalid_argument);
}

TEST_CASE("rejected hello-ack round-trips for every reject reason",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (IpcHelloRejectReason reason : {IpcHelloRejectReason::kInvalidProof,
                                      IpcHelloRejectReason::kMalformed}) {
    IpcHelloAckMessage original{
        .correlationId = 1, .accepted = false, .rejectReason = reason};

    auto result = EncodeThenDecode(codec, IpcMessage{original});

    REQUIRE(result.has_value());
    CHECK(*result == IpcMessage{original});
  }
}

TEST_CASE("resynchronize-request round-trips", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcResynchronizeRequestMessage original{.correlationId = 42};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("resynchronize-result round-trips for both outcomes",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (bool accepted : {true, false}) {
    IpcResynchronizeResultMessage original{.correlationId = 42,
                                           .accepted = accepted};

    auto result = EncodeThenDecode(codec, IpcMessage{original});

    REQUIRE(result.has_value());
    CHECK(*result == IpcMessage{original});
  }
}

TEST_CASE("close round-trips for every close reason",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (IpcCloseReason reason :
       {IpcCloseReason::kNormal, IpcCloseReason::kShutdown,
        IpcCloseReason::kError}) {
    IpcCloseMessage original{.correlationId = 0, .reason = reason};

    auto result = EncodeThenDecode(codec, IpcMessage{original});

    REQUIRE(result.has_value());
    CHECK(*result == IpcMessage{original});
  }
}

TEST_CASE("reject round-trips for every reject reason",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (IpcRejectReason reason : {
           IpcRejectReason::kMalformedFrameLength,
           IpcRejectReason::kUnknownMessageKind,
           IpcRejectReason::kInvalidIdentity,
           IpcRejectReason::kMalformedPayload,
       }) {
    IpcRejectMessage original{.correlationId = 5, .reason = reason};

    auto result = EncodeThenDecode(codec, IpcMessage{original});

    REQUIRE(result.has_value());
    CHECK(*result == IpcMessage{original});
  }
}

TEST_CASE("cancel round-trips", "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcCancelMessage original{.correlationId = 5};

  auto result = EncodeThenDecode(codec, IpcMessage{original});

  REQUIRE(result.has_value());
  CHECK(*result == IpcMessage{original});
}

TEST_CASE("encoding a cancel with zero correlation id throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;

  CHECK_THROWS_AS(
      codec.Encode(IpcMessage{IpcCancelMessage{.correlationId = 0}}),
      std::invalid_argument);
}

TEST_CASE("maximum correlation id round-trips exactly",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  constexpr std::uint64_t kMaxCorrelationId =
      std::numeric_limits<std::uint64_t>::max();

  auto result = EncodeThenDecode(
      codec, IpcMessage{IpcCancelMessage{.correlationId = kMaxCorrelationId}});

  REQUIRE(result.has_value());
  auto *cancel = std::get_if<IpcCancelMessage>(&*result);
  REQUIRE(cancel != nullptr);
  CHECK(cancel->correlationId == kMaxCorrelationId);
}

//  ---- Encode failures ----

TEST_CASE("encoding a hello with an oversized token throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> oversizedToken(kMaxIpcPeerProofTokenBytes + 1);
  IpcHelloMessage message{.correlationId = 1,
                          .adapterInstanceId = SampleInstanceId(),
                          .peerProofToken = oversizedToken};

  CHECK_THROWS_AS(codec.Encode(IpcMessage{message}), std::invalid_argument);
}

TEST_CASE("encoding a close with nonzero correlation id throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;

  CHECK_THROWS_AS(codec.Encode(IpcMessage{IpcCloseMessage{
                      .correlationId = 1, .reason = IpcCloseReason::kNormal}}),
                  std::invalid_argument);
}

//  ---- TryReadFrameLength ----

TEST_CASE("a header-only declared length is accepted",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::array<std::byte, 4> prefix =
      LittleEndianLength(static_cast<std::uint32_t>(kIpcFrameHeaderBytes));

  auto frameLength = codec.TryReadFrameLength(prefix);

  REQUIRE(frameLength.has_value());
  CHECK(*frameLength == kIpcFrameHeaderBytes);
}

TEST_CASE("a declared length below the header size is rejected",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::array<std::byte, 4> prefix =
      LittleEndianLength(static_cast<std::uint32_t>(kIpcFrameHeaderBytes - 1));

  CHECK_FALSE(codec.TryReadFrameLength(prefix).has_value());
}

TEST_CASE("a declared length above the configured limit is rejected without "
          "needing payload bytes",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::array<std::byte, 4> prefix =
      LittleEndianLength(static_cast<std::uint32_t>(kMaxIpcFrameBytes) + 1);

  CHECK_FALSE(codec.TryReadFrameLength(prefix).has_value());
}

TEST_CASE("a declared length exactly at the configured limit is accepted",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::array<std::byte, 4> prefix =
      LittleEndianLength(static_cast<std::uint32_t>(kMaxIpcFrameBytes));

  auto frameLength = codec.TryReadFrameLength(prefix);

  REQUIRE(frameLength.has_value());
  CHECK(*frameLength == kMaxIpcFrameBytes);
}

TEST_CASE("a length prefix of the wrong byte count is rejected",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;

  CHECK_FALSE(codec.TryReadFrameLength(std::vector<std::byte>(3)).has_value());
  CHECK_FALSE(codec.TryReadFrameLength(std::vector<std::byte>(5)).has_value());
}

//  ---- Decode failures ----

TEST_CASE("a frame declaring an unrecognized message kind fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::byte kindByte : {std::byte{0}, std::byte{8}, std::byte{250}}) {
    std::vector<std::byte> frame =
        codec.Encode(IpcMessage{IpcCancelMessage{.correlationId = 1}});
    frame[4] = kindByte;

    auto result = codec.Decode(std::span(frame).subspan(4));

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kUnknownMessageKind);
  }
}

TEST_CASE("fewer bytes than one header fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;

  auto result = codec.Decode(std::vector<std::byte>(kIpcFrameHeaderBytes - 1));

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedFrameLength);
}

TEST_CASE(
    "Decode independently rejects a frame longer than the configured limit",
    "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;

  auto result = codec.Decode(std::vector<std::byte>(kMaxIpcFrameBytes + 1));

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedFrameLength);
}

TEST_CASE("a hello payload of the wrong length fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  IpcHelloMessage message{.correlationId = 1,
                          .adapterInstanceId = SampleInstanceId(),
                          .peerProofToken = {std::byte{9}, std::byte{9}}};
  std::vector<std::byte> frame = codec.Encode(IpcMessage{message});
  frame.pop_back();
  std::uint32_t newLength = static_cast<std::uint32_t>(frame.size() - 4);
  std::array<std::byte, 4> lengthBytes = LittleEndianLength(newLength);
  std::ranges::copy(lengthBytes, frame.begin());

  auto result = codec.Decode(std::span(frame).subspan(4));

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a hello payload exactly 16 bytes (missing the token-length byte) "
          "fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kHello, 1, std::vector<std::byte>(16));

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a hello payload whose declared token length exceeds the bound fails "
          "closed as an invalid identity",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> payload(17);
  payload[16] = static_cast<std::byte>(kMaxIpcPeerProofTokenBytes + 1);
  std::vector<std::byte> frame = BuildFrame(IpcMessageKind::kHello, 1, payload);

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kInvalidIdentity);
}

TEST_CASE("a hello-ack payload with an out-of-range accepted byte fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> payload{std::byte{2}, std::byte{0}};
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kHelloAck, 1, payload);

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("an accepted hello-ack with a reject reason fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kHelloAck, 1,
                 {std::byte{1}, std::byte{static_cast<std::uint8_t>(
                                    IpcHelloRejectReason::kInvalidProof)}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a rejected hello-ack without a reject reason fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kHelloAck, 1, {std::byte{0}, std::byte{0}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a hello-ack payload with an unrecognized reject reason fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::byte reasonByte : {std::byte{4}, std::byte{250}}) {
    std::vector<std::byte> payload{std::byte{0}, reasonByte};
    std::vector<std::byte> frame =
        BuildFrame(IpcMessageKind::kHelloAck, 1, payload);

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a hello-ack payload of the wrong length fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::size_t payloadSize :
       {std::size_t{0}, std::size_t{1}, std::size_t{3}}) {
    std::vector<std::byte> frame = BuildFrame(
        IpcMessageKind::kHelloAck, 1, std::vector<std::byte>(payloadSize));

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a resynchronize-request carrying an unexpected payload fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kResynchronizeRequest, 1, {std::byte{0}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a resynchronize-result payload with an out-of-range accepted byte "
          "fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kResynchronizeResult, 1, {std::byte{2}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a close payload with an unrecognized reason fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::byte reasonByte : {std::byte{3}, std::byte{250}}) {
    std::vector<std::byte> frame =
        BuildFrame(IpcMessageKind::kClose, 0, {reasonByte});

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a close with nonzero correlation id fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame = BuildFrame(
      IpcMessageKind::kClose, 1,
      {std::byte{static_cast<std::uint8_t>(IpcCloseReason::kNormal)}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a close payload of the wrong length fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::size_t payloadSize : {std::size_t{0}, std::size_t{2}}) {
    std::vector<std::byte> frame = BuildFrame(
        IpcMessageKind::kClose, 0, std::vector<std::byte>(payloadSize));

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a reject payload with an unrecognized reason fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::byte reasonByte : {std::byte{5}, std::byte{250}}) {
    std::vector<std::byte> frame =
        BuildFrame(IpcMessageKind::kReject, 1, {reasonByte});

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a reject payload of the wrong length fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  for (std::size_t payloadSize : {std::size_t{0}, std::size_t{2}}) {
    std::vector<std::byte> frame = BuildFrame(
        IpcMessageKind::kReject, 1, std::vector<std::byte>(payloadSize));

    auto result = codec.Decode(frame);

    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == IpcRejectReason::kMalformedPayload);
  }
}

TEST_CASE("a cancel message carrying an unexpected payload fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      BuildFrame(IpcMessageKind::kCancel, 1, {std::byte{0}});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

TEST_CASE("a cancel with zero correlation id fails closed",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame = BuildFrame(IpcMessageKind::kCancel, 0, {});

  auto result = codec.Decode(frame);

  REQUIRE_FALSE(result.has_value());
  CHECK(result.error() == IpcRejectReason::kMalformedPayload);
}

//  ---- Idempotence and robustness ----

TEST_CASE("decoding the same close frame repeatedly is side-effect-free and "
          "never throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame = codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});

  auto first = codec.Decode(std::span(frame).subspan(4));
  auto second = codec.Decode(std::span(frame).subspan(4));

  REQUIRE(first.has_value());
  REQUIRE(second.has_value());
  CHECK(*first == *second);
}

TEST_CASE("decoding the same cancel frame repeatedly is side-effect-free and "
          "never throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::byte> frame =
      codec.Encode(IpcMessage{IpcCancelMessage{.correlationId = 3}});

  auto first = codec.Decode(std::span(frame).subspan(4));
  auto second = codec.Decode(std::span(frame).subspan(4));

  REQUIRE(first.has_value());
  REQUIRE(second.has_value());
  CHECK(*first == *second);
}

TEST_CASE("decoding arbitrary, entirely untrusted byte content never throws",
          "[ipc][ipc_frame_codec]") {
  IpcFrameCodec codec;
  std::vector<std::vector<std::byte>> garbageFrames = {
      {},
      {std::byte{0xFF}},
      std::vector<std::byte>(10),
      {std::byte{1}, std::byte{250}, std::byte{0}, std::byte{0}, std::byte{0},
       std::byte{0}, std::byte{0}, std::byte{0}, std::byte{0}, std::byte{0},
       std::byte{1}, std::byte{2}, std::byte{3}},
  };

  for (const auto &garbage : garbageFrames) {
    CHECK_NOTHROW(codec.Decode(garbage));
  }
}

TEST_CASE("no ipc header includes a Skyrim or SKSE runtime header",
          "[ipc][ipc_frame_codec][structural]") {
  //  Structural pin, not a functional assertion: concept 01 must never depend
  //  on CommonLib/Skyrim, per ai/context/adapter/architecture.md's "Technology
  //  boundary". Reads every adapter/ipc/*.hpp source file's own text and checks
  //  it, so a future edit that reintroduces an RE/... or SKSE/... include fails
  //  this test even though nothing here links CommonLibSSE.
  std::filesystem::path ipcDir{DOVAHLINK_ADAPTER_IPC_DIR};
  REQUIRE(std::filesystem::exists(ipcDir));

  int headerCount = 0;
  for (const auto &entry : std::filesystem::directory_iterator(ipcDir)) {
    if (entry.path().extension() != ".hpp") {
      continue;
    }
    ++headerCount;

    std::ifstream file(entry.path());
    REQUIRE(file.is_open());
    std::ostringstream contents;
    contents << file.rdbuf();
    std::string text = contents.str();

    INFO("checking " << entry.path().filename().string());
    CHECK(text.find("RE/") == std::string::npos);
    CHECK(text.find("SKSE/") == std::string::npos);
  }

  CHECK(headerCount > 0);
}
