#pragma once

#include <cstdint>

#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by either side to announce a deterministic close of the private
///  channel. Decoding and encoding this message is side-effect-free; a receiver
///  may observe it more than once for the same logical close without that being
///  an error.
struct IpcCloseMessage {
  ///  Always zero; a close is unsolicited and expects no reply.
  std::uint64_t correlationId = 0;
  ///  Why the sender is closing the channel.
  IpcCloseReason reason = IpcCloseReason::kNormal;

  ///  Structural equality over every field.
  bool operator==(const IpcCloseMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
