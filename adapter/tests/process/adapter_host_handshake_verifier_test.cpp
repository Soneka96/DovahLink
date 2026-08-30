#include "process/adapter_host_handshake_verifier.hpp"

#include "ipc/adapter_ipc_hmac.hpp"
#include "ipc/adapter_ipc_socket_test_support.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <future>
#include <optional>
#include <span>
#include <utility>
#include <variant>
#include <vector>

using dovahlink::adapter::identity::AdapterInstanceId;
using dovahlink::adapter::ipc::BuildHostProofMessage;
using dovahlink::adapter::ipc::ComputeIpcHmacSha256;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;
using dovahlink::adapter::ipc::test_support::FakeAdapterIpcSocket;
using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::AdapterHostHandshakeVerifier;

namespace {

///  A representative, fixed adapter instance id for tests that don't care
///  about its value.
AdapterInstanceId SampleInstanceId() {
  AdapterInstanceId id{};
  for (std::size_t index = 0; index < id.value.size(); ++index) {
    id.value[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

///  A representative, fixed owner-lifetime-id for tests that don't care about
///  its value.
std::array<std::byte, kIpcOwnerLifetimeIdBytes> SampleOwnerLifetimeId() {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(100 + index);
  }
  return id;
}

///  A representative candidate endpoint whose proof token this fixture's
///  verifier is expected to present in its Hello.
AdapterHostEndpoint SampleCandidate() {
  return AdapterHostEndpoint{
      .port = 12345, .proofToken = {std::byte{9}, std::byte{8}, std::byte{7}}};
}

///  Waits for `future` up to a generous bound and returns whether it became
///  ready, instead of hanging the test suite if production code regresses.
template <typename T> bool WaitReady(std::future<T> &future) {
  return future.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
}

///  Decodes the one frame written to `socket` so far, then clears its
///  recorded written bytes so a later round's write starts from empty.
IpcMessage DecodeOneWrittenFrame(FakeAdapterIpcSocket &socket,
                                 const IpcFrameCodec &codec) {
  std::vector<std::byte> bytes = socket.WrittenBytes();
  socket.ClearWrittenBytes();
  REQUIRE(bytes.size() >= sizeof(std::uint32_t));
  std::optional<std::size_t> frameLength =
      codec.TryReadFrameLength(std::span(bytes).first(sizeof(std::uint32_t)));
  REQUIRE(frameLength.has_value());
  REQUIRE(bytes.size() == sizeof(std::uint32_t) + *frameLength);
  auto decoded = codec.Decode(std::span(bytes).subspan(sizeof(std::uint32_t)));
  REQUIRE(decoded.has_value());
  return *decoded;
}

///  Bundles a verifier with the fake socket and real codec it was
///  constructed against, so each test can inspect and drive them without
///  repeating setup. Uses a short verify timeout so a timeout test does not
///  slow down the suite.
struct VerifierFixture {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterHostHandshakeVerifier verifier{SampleInstanceId(),
                                        SampleOwnerLifetimeId(), socket, codec,
                                        std::chrono::milliseconds(200)};
};

///  Starts `fixture.verifier.Verify(candidate)` on a background thread (it
///  blocks on socket I/O) and waits for its outbound Hello to land, then
///  returns that decoded Hello and the still-running result future. Used by
///  every test that must react to the Hello's fresh challenge/correlation id
///  before delivering a reply.
std::pair<IpcHelloMessage, std::future<bool>>
StartVerifyAndCaptureHello(VerifierFixture &fixture,
                           const AdapterHostEndpoint &candidate) {
  std::promise<void> helloSent;
  fixture.socket.SetWriteCompletedSignal(helloSent);
  std::future<void> helloSentFuture = helloSent.get_future();

  std::future<bool> result = std::async(
      std::launch::async, [&] { return fixture.verifier.Verify(candidate); });

  REQUIRE(WaitReady(helloSentFuture));
  IpcMessage sent = DecodeOneWrittenFrame(fixture.socket, fixture.codec);
  auto *hello = std::get_if<IpcHelloMessage>(&sent);
  REQUIRE(hello != nullptr);
  return {*hello, std::move(result)};
}

///  Computes the accepted HelloAck a legitimate host would produce for
///  `hello`, using `candidate`'s own proof token as the HMAC key.
IpcHelloAckMessage BuildAcceptedHelloAck(const IpcHelloMessage &hello,
                                         const AdapterHostEndpoint &candidate) {
  auto expectedProof = ComputeIpcHmacSha256(
      candidate.proofToken,
      BuildHostProofMessage(hello.challenge, hello.correlationId,
                            hello.adapterInstanceId, hello.ownerLifetimeId));
  return IpcHelloAckMessage{.correlationId = hello.correlationId,
                            .accepted = true,
                            .rejectReason = IpcHelloRejectReason::kNone,
                            .hostProof = expectedProof};
}

} //  namespace

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns true for a candidate "
          "with a correct, accepted hostProof") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  auto [hello, result] = StartVerifyAndCaptureHello(fixture, candidate);
  fixture.socket.PushReadableBytes(fixture.codec.Encode(
      IpcMessage{BuildAcceptedHelloAck(hello, candidate)}));

  REQUIRE(WaitReady(result));
  CHECK(result.get());
  CHECK(fixture.socket.CloseCallCount() == 1);
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when accepted "
          "is false even with an otherwise-correct hostProof") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  auto [hello, result] = StartVerifyAndCaptureHello(fixture, candidate);
  IpcHelloAckMessage ack = BuildAcceptedHelloAck(hello, candidate);
  ack.accepted = false;
  ack.rejectReason = IpcHelloRejectReason::kInvalidProof;
  fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{ack}));

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when an "
          "accepted HelloAck's hostProof is forged, wrong, or missing") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  auto [hello, result] = StartVerifyAndCaptureHello(fixture, candidate);

  SECTION("missing (all-zero) hostProof") {
    fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello.correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone}}));
  }

  SECTION("forged (arbitrary) hostProof") {
    std::array<std::byte, 32> forged{};
    forged.fill(std::byte{0xAB});
    fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello.correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = forged}}));
  }

  SECTION("wrong hostProof (computed with the wrong key)") {
    auto wrongProof = ComputeIpcHmacSha256(
        std::vector<std::byte>{std::byte{1}, std::byte{2}, std::byte{3}},
        BuildHostProofMessage(hello.challenge, hello.correlationId,
                              hello.adapterInstanceId, hello.ownerLifetimeId));
    fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello.correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = wrongProof}}));
  }

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when an "
          "accepted HelloAck's correlation id does not match the Hello it "
          "sent") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  auto [hello, result] = StartVerifyAndCaptureHello(fixture, candidate);

  //  A genuinely-correct proof for a *different* correlation id -- the
  //  mismatch alone must reject it, even though the proof math is otherwise
  //  valid for that other id.
  auto proofForDifferentCorrelationId = ComputeIpcHmacSha256(
      candidate.proofToken,
      BuildHostProofMessage(hello.challenge, hello.correlationId + 1,
                            hello.adapterInstanceId, hello.ownerLifetimeId));
  fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = hello.correlationId + 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone,
                         .hostProof = proofForDifferentCorrelationId}}));

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false, without "
          "throwing, when the candidate is unreachable") {
  VerifierFixture fixture;
  fixture.socket.SetConnectResults({false});

  CHECK_FALSE(fixture.verifier.Verify(SampleCandidate()));
  CHECK(fixture.socket.ConnectCallCount() == 1);
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when the "
          "candidate never responds within the bound") {
  VerifierFixture fixture;

  //  Nothing is ever pushed to the socket, so every read poll returns zero
  //  bytes until the fixture's short verify timeout elapses.
  CHECK_FALSE(fixture.verifier.Verify(SampleCandidate()));
  CHECK(fixture.socket.CloseCallCount() == 1);
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when the "
          "candidate responds with an unexpected message kind") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  std::future<bool> result =
      StartVerifyAndCaptureHello(fixture, candidate).second;
  //  A close message always carries correlation id zero, matching how
  //  AdapterIpcSession itself sends one.
  fixture.socket.PushReadableBytes(fixture.codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}}));

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false, without "
          "throwing, when Connect throws") {
  VerifierFixture fixture;
  std::promise<void> attempted;
  fixture.socket.SetThrowOnConnect(attempted);

  CHECK_FALSE(fixture.verifier.Verify(SampleCandidate()));
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when writing "
          "the Hello fails") {
  VerifierFixture fixture;
  fixture.socket.RequestStop();

  CHECK_FALSE(fixture.verifier.Verify(SampleCandidate()));
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when the "
          "candidate disconnects before responding") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  std::future<bool> result =
      StartVerifyAndCaptureHello(fixture, candidate).second;
  fixture.socket.SimulateDisconnect();

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify returns false when the "
          "candidate's response fails to decode") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  std::future<bool> result =
      StartVerifyAndCaptureHello(fixture, candidate).second;
  //  A length prefix declaring an out-of-range frame length fails
  //  `TryReadFrameLength` before any payload is read.
  fixture.socket.PushReadableBytes(
      {std::byte{0xFF}, std::byte{0xFF}, std::byte{0xFF}, std::byte{0xFF}});

  REQUIRE(WaitReady(result));
  CHECK_FALSE(result.get());
}

TEST_CASE("AdapterHostHandshakeVerifier::Verify issues a fresh, incrementing "
          "correlation id for every call") {
  VerifierFixture fixture;
  AdapterHostEndpoint candidate = SampleCandidate();

  auto [firstHello, firstResult] =
      StartVerifyAndCaptureHello(fixture, candidate);
  CHECK(firstHello.correlationId == 1);
  fixture.socket.PushReadableBytes(fixture.codec.Encode(
      IpcMessage{BuildAcceptedHelloAck(firstHello, candidate)}));
  REQUIRE(WaitReady(firstResult));
  CHECK(firstResult.get());

  auto [secondHello, secondResult] =
      StartVerifyAndCaptureHello(fixture, candidate);
  CHECK(secondHello.correlationId == 2);
  fixture.socket.PushReadableBytes(fixture.codec.Encode(
      IpcMessage{BuildAcceptedHelloAck(secondHello, candidate)}));
  REQUIRE(WaitReady(secondResult));
  CHECK(secondResult.get());
}
