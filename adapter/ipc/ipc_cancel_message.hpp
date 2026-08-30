#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by either side to cancel a previously sent request identified by
///  `correlationId`. Cancelling a request that already completed or was already
///  cancelled is a harmless no-op, not an error, at this wire-contract layer.
struct IpcCancelMessage {
  ///  The correlation id of the request being cancelled.
  std::uint64_t correlationId = 0;

  ///  Structural equality over every field.
  bool operator==(const IpcCancelMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
