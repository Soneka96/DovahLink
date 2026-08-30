#pragma once

#include <chrono>

namespace dovahlink::adapter::process {

//  ---- Handshake verification ----

///  The default bound `AdapterHostHandshakeVerifier` waits for a candidate's
///  `IpcHelloAckMessage` before treating it as an unreachable or
///  non-responding peer. Approved as a provisional value for this concept's
///  loopback-only, same-machine handshake; a later concept may revise it with
///  the same documented approval `ai/context/protocol/security.md`'s own
///  limits require.
inline constexpr std::chrono::milliseconds
    kDefaultAdapterHostHandshakeVerifyTimeout{2000};

} //  namespace dovahlink::adapter::process
