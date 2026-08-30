#pragma once

#include "identity/adapter_instance_id.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

namespace dovahlink::adapter::ipc {
class IAdapterIpcSocket;
} //  namespace dovahlink::adapter::ipc

namespace dovahlink::adapter::process {

///  Verifies one candidate private-IPC endpoint against the same mutual-auth
///  conjunction `ipc::AdapterIpcSession` enforces for the adapter's long-lived
///  connection, over a throwaway, one-shot socket. Discovery (rendezvous
///  reading or process launch) only ever produces a candidate, never proof;
///  a candidate must still pass this verification before it is trusted as
///  the intended host.
class IAdapterHostHandshakeVerifier {
public:
  virtual ~IAdapterHostHandshakeVerifier() = default;

  ///  Performs one bounded, self-contained connect + Hello + HelloAck-read
  ///  against `candidate`, then closes the connection. Never touches the
  ///  adapter's long-lived `IAdapterIpcSession`/`IAdapterIpcConnection` pair.
  ///  @return `true` only when `candidate` proves the full mutual-auth
  ///  conjunction: accepted, matching correlation id, and a verifying
  ///  `hostProof`. `false` for a rejected, non-responding, unreachable, or
  ///  malformed peer; never throws.
  virtual bool Verify(const AdapterHostEndpoint &candidate) = 0;
};

///  @copydoc IAdapterHostHandshakeVerifier
class AdapterHostHandshakeVerifier final
    : public IAdapterHostHandshakeVerifier {
public:
  ///  Creates a verifier for one adapter process's repeated candidate checks.
  ///  @param instanceId This adapter process's own instance identity.
  ///  @param ownerLifetimeId The owning Skyrim process's lifetime identity.
  ///  @param socket A dedicated one-shot socket: the caller retargets its
  ///  port (through the concrete socket type's own port-setting method)
  ///  before each `Verify` call and never shares this instance with the
  ///  long-lived session/connection.
  ///  @param codec Encodes the Hello and decodes the candidate's response.
  ///  @param verifyTimeout The bound on waiting for a candidate's response.
  AdapterHostHandshakeVerifier(
      identity::AdapterInstanceId instanceId,
      std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
      ipc::IAdapterIpcSocket &socket, const ipc::IIpcFrameCodec &codec,
      std::chrono::milliseconds verifyTimeout =
          kDefaultAdapterHostHandshakeVerifyTimeout);

  ///  @copydoc IAdapterHostHandshakeVerifier::Verify
  bool Verify(const AdapterHostEndpoint &candidate) override;

private:
  ///  Reads and decodes exactly one frame, bounded by `deadline`.
  ///  @return The decoded message, or `std::nullopt` if the connection ended,
  ///  the frame failed to decode, or `deadline` elapsed first.
  std::optional<ipc::IpcMessage>
  ReadOneMessageWithDeadline(std::chrono::steady_clock::time_point deadline);

  ///  Fills `buffer` completely, polling `socket_` and checking `deadline`
  ///  between each poll.
  ///  @return `false` if the connection ended or `deadline` elapsed before
  ///  `buffer` was completely filled.
  bool ReadFullyWithDeadline(std::span<std::byte> buffer,
                             std::chrono::steady_clock::time_point deadline);

  ///  Issues the next monotonic Hello correlation id for this verifier,
  ///  starting at 1.
  std::uint64_t NextCorrelationId();

  ///  This adapter process's own instance identity.
  identity::AdapterInstanceId instanceId_;
  ///  The owning Skyrim process's lifetime identity.
  std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId_;
  ///  The dedicated one-shot socket this verifier connects and reads/writes
  ///  through.
  ipc::IAdapterIpcSocket &socket_;
  ///  Encodes the Hello and decodes the candidate's response.
  const ipc::IIpcFrameCodec &codec_;
  ///  The bound on waiting for a candidate's response.
  std::chrono::milliseconds verifyTimeout_;
  ///  The most recently issued Hello correlation id.
  std::uint64_t nextCorrelationId_ = 0;
};

} //  namespace dovahlink::adapter::process
