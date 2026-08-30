#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace dovahlink::adapter::process {

///  A candidate private-IPC endpoint discovered via the packaged-host
///  startup rendezvous: the port to connect to and the peer-proof token to
///  present. Discovery only, never authentication -- a candidate must still
///  pass the full mutual Hello/HelloAck handshake before it is trusted as
///  the intended host.
struct AdapterHostEndpoint {
  ///  The loopback port to connect to.
  std::uint16_t port = 0;
  ///  The peer-ownership proof to present in the Hello.
  std::vector<std::byte> proofToken;

  ///  Structural equality over every field.
  bool operator==(const AdapterHostEndpoint &) const = default;
};

} //  namespace dovahlink::adapter::process
