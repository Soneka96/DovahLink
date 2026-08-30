#pragma once

#include <functional>

#include "ipc/ipc_message.hpp"

namespace dovahlink::adapter::ipc {

///  The connection lifecycle event hooks `AdapterIpcConnection` invokes on
///  its owner. Every field belongs to the same contract: how the transport
///  reports what is happening on the channel, since it makes no protocol
///  decisions of its own. Every callback may be invoked from the
///  connection's own background thread.
struct AdapterIpcConnectionCallbacks {
  ///  Invoked once per successful connect, before frames are served.
  std::function<void()> onConnected;
  ///  Invoked for each successfully decoded inbound message.
  std::function<void(const IpcMessage &)> onMessageReceived;
  ///  Invoked when an inbound frame could not be decoded.
  std::function<void()> onDecodeFailure;
  ///  Invoked once a connected session ends, before a reconnect attempt or
  ///  the connection stopping entirely.
  std::function<void()> onDisconnected;
};

} //  namespace dovahlink::adapter::ipc
