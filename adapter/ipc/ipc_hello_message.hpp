#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace dovahlink::adapter::ipc {

///  Sent by the connecting adapter to negotiate the private channel: the
///  requested protocol version (carried in the frame header, not this payload),
///  the adapter's own instance identity, and a bounded peer-ownership proof
///  token. The proof's expected value and comparison policy belong to the host
///  channel that consumes this message, not to this wire contract.
struct IpcHelloMessage {
  ///  Pairs this request with its `IpcHelloAckMessage` response.
  std::uint64_t correlationId = 0;
  ///  The connecting adapter's instance identity, as 16 opaque bytes.
  std::array<std::byte, 16> adapterInstanceId{};
  ///  The bounded peer-ownership proof, at most `kMaxIpcPeerProofTokenBytes`
  ///  bytes.
  std::vector<std::byte> peerProofToken;

  ///  Structural equality over every field.
  bool operator==(const IpcHelloMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
