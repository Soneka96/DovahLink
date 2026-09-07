#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the host to present a no-code terminal notification once the
///  wrong-attempt hard limit has cancelled the active pairing challenge. Best
///  effort and unsolicited: the adapter sends no reply, and a missing or
///  unavailable adapter never blocks the client response that already
///  reported the outcome.
struct IpcPairingAttemptsExhaustedMessage {
  ///  Always zero; this notification is unsolicited and expects no reply.
  std::uint64_t correlationId = 0;

  ///  Structural equality over every field.
  bool operator==(const IpcPairingAttemptsExhaustedMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
