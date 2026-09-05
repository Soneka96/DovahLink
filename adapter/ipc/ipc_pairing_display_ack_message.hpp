#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the adapter in response to `IpcPairingDisplayMessage`, reporting
///  only whether its Skyrim-facing display seam accepted the request. Initial
///  display and manual redisplay gate a public outcome on this
///  acknowledgement; wrong-code automatic redisplay is best effort and its
///  caller may discard this result.
struct IpcPairingDisplayAckMessage {
  ///  Matches the `IpcPairingDisplayMessage` this responds to.
  std::uint64_t correlationId = 0;
  ///  Whether the adapter's display seam accepted and presented the code.
  bool accepted = false;

  ///  Structural equality over every field.
  bool operator==(const IpcPairingDisplayAckMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
