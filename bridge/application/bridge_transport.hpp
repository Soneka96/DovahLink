#pragma once

#include "application/coordinator.hpp"
#include "transport/listener.hpp"

namespace dovahlink::application {

/// Provides the coordinator's transport lifecycle for two already-bound
/// loopback listeners. The synchronous bridge has no asynchronous completions,
/// and accept workers own admission.
class BridgeTransport : public TransportLifecycle {
public:
  /// Keeps references to the already-bound IPv4 and IPv6 listeners.
  /// @param listenerV4 IPv4 loopback listener owned by the caller.
  /// @param listenerV6 IPv6 loopback listener owned by the caller.
  BridgeTransport(transport::LoopbackListener &listenerV4,
                  transport::LoopbackListener &listenerV6);

  /// Leaves the already-bound listeners unchanged.
  void Start() override;

  /// The synchronous transport has no pending completions to cancel.
  void CancelCompletions() override;

  /// Closes both listener acceptors; repeated calls are harmless.
  void Close() override;

private:
  /// IPv4 listener referenced by this lifecycle adapter.
  transport::LoopbackListener &listenerV4_;

  /// IPv6 listener referenced by this lifecycle adapter.
  transport::LoopbackListener &listenerV6_;
};

} // namespace dovahlink::application
