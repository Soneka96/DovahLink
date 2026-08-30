#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by the host in response to `IpcHelloMessage` to conclude negotiation.
///  `accepted = true` alone never proves the responder is the legitimate
///  host -- it only proves the responder checked the adapter's presented
///  proof -- so the receiver must independently verify `hostProof` and never
///  trust `accepted` on its own.
struct IpcHelloAckMessage {
  ///  Matches the `IpcHelloMessage` this responds to.
  std::uint64_t correlationId = 0;
  ///  Whether the host accepted the connection.
  bool accepted = false;
  ///  The negotiation failure reason when `accepted` is `false`; otherwise
  ///  `IpcHelloRejectReason::kNone`.
  IpcHelloRejectReason rejectReason = IpcHelloRejectReason::kNone;
  ///  `HMAC-SHA256(key = peerProofToken, message = challenge ||
  ///  correlationId || adapterInstanceId || ownerLifetimeId)`, proving the
  ///  host holds the shared secret. All-zero when `accepted` is `false` --
  ///  the host does not compute a real proof for a connection it is
  ///  refusing.
  std::array<std::byte, kIpcHostProofBytes> hostProof{};

  ///  Structural equality over every field.
  bool operator==(const IpcHelloAckMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
