#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the adapter in response to `IpcResynchronizeRequestMessage`.
///  Reports only whether the adapter could service the request; the fresh
///  baseline data itself is a later concept's contract.
struct IpcResynchronizeResultMessage {
  ///  Matches the `IpcResynchronizeRequestMessage` this responds to.
  std::uint64_t correlationId = 0;
  ///  Whether the adapter could capture and will deliver a fresh baseline.
  bool accepted = false;

  ///  Structural equality over every field.
  bool operator==(const IpcResynchronizeResultMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
