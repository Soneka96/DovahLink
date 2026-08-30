#pragma once

#include <cstdint>

#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by the host in response to `IpcHelloMessage` to conclude negotiation.
struct IpcHelloAckMessage {
  ///  Matches the `IpcHelloMessage` this responds to.
  std::uint64_t correlationId = 0;
  ///  Whether the host accepted the connection.
  bool accepted = false;
  ///  The negotiation failure reason when `accepted` is `false`; otherwise
  ///  `IpcHelloRejectReason::kNone`.
  IpcHelloRejectReason rejectReason = IpcHelloRejectReason::kNone;

  ///  Structural equality over every field.
  bool operator==(const IpcHelloAckMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
