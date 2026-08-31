#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace dovahlink::adapter::process {

///  A candidate private-IPC endpoint discovered via the packaged-host
///  startup rendezvous: the port to connect to, the peer-proof token to
///  present, and the host's own HostProof HMAC key. Discovery only, never
///  authentication -- a candidate must still pass the full mutual
///  Hello/HelloAck handshake before it is trusted as the intended host.
struct AdapterHostEndpoint {
  ///  The loopback port to connect to.
  std::uint16_t port = 0;
  ///  The peer-ownership proof to present in the Hello.
  std::vector<std::byte> proofToken;
  ///  The host's independent HostProof HMAC key, used locally to verify a
  ///  received HelloAck's HostProof -- never presented on the wire, unlike
  ///  `proofToken`.
  std::vector<std::byte> hostProofKey;

  ///  Structural equality over every field.
  bool operator==(const AdapterHostEndpoint &) const = default;
};

} //  namespace dovahlink::adapter::process
