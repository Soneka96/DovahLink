#pragma once

#include "transport/loopback_listener.hpp"

namespace dovahlink::application {

///  Controls transport startup, completion cancellation, and closure.
class IBridgeTransport {
  public:
    ///  Releases the interface without performing work.
    virtual ~IBridgeTransport() = default;

    ///  Starts transport handling.
    virtual void Start() = 0;

    ///  Cancels transport completions that can still run.
    virtual void CancelCompletions() = 0;

    ///  Closes transport resources.
    virtual void Close() = 0;
};

///  Provides the coordinator's transport lifecycle for two already-bound
///  loopback listeners. The synchronous bridge has no asynchronous completions,
///  and accept workers own admission.
class BridgeTransport : public IBridgeTransport {
  public:
    ///  Keeps references to the already-bound IPv4 and IPv6 listeners.
    ///  @param listenerV4 IPv4 loopback listener owned by the caller.
    ///  @param listenerV6 IPv6 loopback listener owned by the caller.
    BridgeTransport(transport::ILoopbackListener& listenerV4,
                    transport::ILoopbackListener& listenerV6);

    ///  Leaves the already-bound listeners unchanged.
    void Start() override;

    ///  The synchronous transport has no pending completions to cancel.
    void CancelCompletions() override;

    ///  Closes both listener acceptors; repeated calls are harmless.
    void Close() override;

  private:
    ///  IPv4 listener referenced by this lifecycle adapter.
    transport::ILoopbackListener& listenerV4_;

    ///  IPv6 listener referenced by this lifecycle adapter.
    transport::ILoopbackListener& listenerV6_;
};

} //  namespace dovahlink::application
