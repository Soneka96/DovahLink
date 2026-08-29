#pragma once

#include <cstdint>

#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by either side to report that a frame it could still safely decode
///  carried an invalid version, kind, identity, or payload. A frame that cannot
///  be safely decoded at all is never answered this way; the receiver closes
///  immediately instead, per `ai/context/protocol/security.md`'s "Failure
///  behavior".
struct IpcRejectMessage {
  ///  Matches the rejected message's correlation id, or zero if it had none.
  std::uint64_t correlationId = 0;
  ///  Why the message was rejected.
  IpcRejectReason reason = IpcRejectReason::kMalformedFrameLength;

  ///  Structural equality over every field.
  bool operator==(const IpcRejectMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
