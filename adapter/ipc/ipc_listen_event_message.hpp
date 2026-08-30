#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Asks the adapter to listen for one host-owned opaque event key.
struct IpcListenEventMessage {
  ///  The nonzero request identity used for cancellation and diagnostics.
  std::uint64_t correlationId = 0;
  ///  The nonzero key the adapter maps at the final Skyrim boundary.
  std::uint32_t eventKey = 0;

  ///  Structural equality over every field.
  bool operator==(const IpcListenEventMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
