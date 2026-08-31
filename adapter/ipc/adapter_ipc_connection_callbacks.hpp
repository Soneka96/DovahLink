#pragma once

#include <functional>

#include "ipc/adapter_ipc_target.hpp"
#include "ipc/ipc_message.hpp"

namespace dovahlink::adapter::ipc {

///  The result of handing one decoded inbound message to the protocol/session
///  owner. The connection uses this only to control its serving lifecycle;
///  the owner remains responsible for deciding whether a message authenticates
///  the peer.
enum class AdapterIpcMessageDisposition {
  ///  Keep serving, with authentication still pending when applicable.
  kContinue,
  ///  The current transport has completed the required authentication.
  kAuthenticated,
  ///  End the current transport generation.
  kClose,
};

///  The connection lifecycle event hooks `AdapterIpcConnection` invokes on
///  its owner. Every field belongs to the same contract: how the transport
///  reports what is happening on the channel, since it makes no protocol
///  decisions of its own. Every callback may be invoked from the
///  connection's own background thread.
struct AdapterIpcConnectionCallbacks {
  ///  Invoked once per successful connect with the exact target snapshot used
  ///  by that connection attempt.
  std::function<void(const AdapterIpcTarget &)> onTargetConnected;
  ///  Invoked once per successful connect, before frames are served.
  std::function<void()> onConnected;
  ///  Invoked for each successfully decoded inbound message.
  ///  @return The owner's disposition for the current transport generation.
  ///  An unset callback defaults to `kContinue`.
  std::function<AdapterIpcMessageDisposition(const IpcMessage &)>
      onMessageReceived;
  ///  Invoked when an inbound frame could not be decoded.
  std::function<void()> onDecodeFailure;
  ///  Invoked once a connected session ends, before a reconnect attempt or
  ///  the connection stopping entirely.
  std::function<void()> onDisconnected;
  ///  Invoked when a connect attempt fails before a session is established.
  ///  This may occur repeatedly while the connection's bounded retry loop is
  ///  running, and may be invoked from the connection's background thread.
  std::function<void()> onConnectionAttemptFailed;
};

} //  namespace dovahlink::adapter::ipc
