#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace dovahlink::adapter::ipc {

///  The complete immutable target used by one adapter connection attempt.
///  Port, proof token, HostProof key, and target generation share one
///  rendezvous snapshot so an attempt cannot authenticate one target while
///  connecting to another.
struct AdapterIpcTarget {
  ///  The loopback port of the candidate host.
  std::uint16_t port = 0;
  ///  The proof token expected by the candidate host.
  std::vector<std::byte> proofToken;
  ///  The candidate host's own HostProof HMAC key, independent of
  ///  `proofToken`. Used locally to verify a received HelloAck's HostProof;
  ///  never presented on the wire.
  std::vector<std::byte> hostProofKey;
  ///  The supervisor-assigned identity of this target configuration.
  std::uint64_t targetGeneration = 0;

  ///  Structural equality over every target field.
  bool operator==(const AdapterIpcTarget &) const = default;
};

} //  namespace dovahlink::adapter::ipc
