#pragma once

#include <cstdint>
#include <string>

#include "ipc/ipc_enums.hpp"

namespace dovahlink::adapter::ipc {

///  Sent by the host to ask the adapter to present a pairing code at its
///  Skyrim-facing display seam: initial display, manual redisplay, or
///  best-effort wrong-code automatic redisplay. The host never discloses
///  `code` through any other channel.
struct IpcPairingDisplayMessage {
  ///  The nonzero request identity the adapter's `IpcPairingDisplayAckMessage`
  ///  reply correlates to.
  std::uint64_t correlationId = 0;
  ///  The exact code to display, always `kPairingChallengeCodeDigits` ASCII
  ///  decimal digits.
  std::string code;
  ///  Which display intent this request carries.
  PairingDisplayMode mode = PairingDisplayMode::kInitial;

  ///  Structural equality over every field.
  bool operator==(const IpcPairingDisplayMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
