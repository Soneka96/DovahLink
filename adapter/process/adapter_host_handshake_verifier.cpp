#include "process/adapter_host_handshake_verifier.hpp"

#include "ipc/adapter_ipc_hmac.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <cstdint>
#include <expected>
#include <type_traits>
#include <variant>
#include <vector>

namespace dovahlink::adapter::process {

AdapterHostHandshakeVerifier::AdapterHostHandshakeVerifier(
    identity::AdapterInstanceId instanceId,
    std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
    ipc::IAdapterIpcSocket &socket, const ipc::IIpcFrameCodec &codec,
    std::chrono::milliseconds verifyTimeout)
    : instanceId_(instanceId), ownerLifetimeId_(ownerLifetimeId),
      socket_(socket), codec_(codec), verifyTimeout_(verifyTimeout) {}

bool AdapterHostHandshakeVerifier::Verify(
    const AdapterHostEndpoint &candidate) {
  bool connected = false;
  try {
    connected = socket_.Connect();
  } catch (...) {
    //  Never escape this call; see the containing catch below for why.
    return false;
  }
  if (!connected) {
    return false;
  }

  bool authenticated = false;
  try {
    std::chrono::steady_clock::time_point deadline =
        std::chrono::steady_clock::now() + verifyTimeout_;
    std::uint64_t correlationId = NextCorrelationId();
    std::array<std::byte, ipc::kIpcChallengeBytes> challenge =
        ipc::GenerateIpcChallenge();

    std::vector<std::byte> frame =
        codec_.Encode(ipc::IpcMessage{ipc::IpcHelloMessage{
            .correlationId = correlationId,
            .adapterInstanceId = instanceId_.value,
            .peerProofToken = candidate.proofToken,
            .challenge = challenge,
            .ownerLifetimeId = ownerLifetimeId_,
        }});

    if (socket_.WriteAll(frame)) {
      std::optional<ipc::IpcMessage> message =
          ReadOneMessageWithDeadline(deadline);
      if (message.has_value()) {
        std::array<std::byte, ipc::kIpcHostProofBytes> expectedProof =
            ipc::ComputeIpcHmacSha256(candidate.proofToken,
                                      ipc::BuildHostProofMessage(
                                          challenge, correlationId,
                                          instanceId_.value, ownerLifetimeId_));
        authenticated = std::visit(
            [&](const auto &value) -> bool {
              using T = std::decay_t<decltype(value)>;
              if constexpr (std::is_same_v<T, ipc::IpcHelloAckMessage>) {
                //  The same conjunction ipc::AdapterIpcSession enforces:
                //  accepted is checked independently of a verifying proof,
                //  neither substitutes for the other.
                return value.accepted && value.correlationId == correlationId &&
                       ipc::ConstantTimeEqual(value.hostProof, expectedProof);
              } else {
                return false;
              }
            },
            *message);
      }
    }
  } catch (...) {
    //  A malformed rendezvous-supplied proof token (for example one
    //  exceeding the encodable Hello length) or any other unexpected
    //  failure here must never escape this call, per
    //  ai/context/skse/cpp-style.md's worker-thread exception-containment
    //  rule -- the caller runs discovery rounds on a background thread.
    //  Treat it as a failed verification instead.
    authenticated = false;
  }

  try {
    socket_.Close();
  } catch (...) {
    //  Contained for the same reason as above: this call never throws.
  }
  return authenticated;
}

std::optional<ipc::IpcMessage>
AdapterHostHandshakeVerifier::ReadOneMessageWithDeadline(
    std::chrono::steady_clock::time_point deadline) {
  std::array<std::byte, sizeof(std::uint32_t)> lengthPrefix{};
  if (!ReadFullyWithDeadline(lengthPrefix, deadline)) {
    return std::nullopt;
  }

  std::optional<std::size_t> frameLength =
      codec_.TryReadFrameLength(lengthPrefix);
  if (!frameLength.has_value()) {
    return std::nullopt;
  }

  std::vector<std::byte> frame(*frameLength);
  if (!ReadFullyWithDeadline(frame, deadline)) {
    return std::nullopt;
  }

  std::expected<ipc::IpcMessage, ipc::IpcRejectReason> decoded =
      codec_.Decode(frame);
  if (!decoded.has_value()) {
    return std::nullopt;
  }
  return *decoded;
}

bool AdapterHostHandshakeVerifier::ReadFullyWithDeadline(
    std::span<std::byte> buffer,
    std::chrono::steady_clock::time_point deadline) {
  std::size_t totalRead = 0;
  while (totalRead < buffer.size()) {
    if (std::chrono::steady_clock::now() >= deadline) {
      return false;
    }

    std::optional<std::size_t> bytesRead =
        socket_.TryReadSome(buffer.subspan(totalRead));
    if (!bytesRead.has_value()) {
      return false;
    }
    totalRead += *bytesRead;
  }
  return true;
}

std::uint64_t AdapterHostHandshakeVerifier::NextCorrelationId() {
  return ++nextCorrelationId_;
}

} //  namespace dovahlink::adapter::process
