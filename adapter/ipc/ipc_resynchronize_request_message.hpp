#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the host to request that the adapter answer with a fresh
///  current-state baseline. Carries no payload of its own; the actual captured
///  baseline is a later concept's contract.
struct IpcResynchronizeRequestMessage {
  ///  Pairs this request with its `IpcResynchronizeResultMessage` response.
  std::uint64_t correlationId = 0;

  ///  Structural equality over every field.
  bool operator==(const IpcResynchronizeRequestMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
