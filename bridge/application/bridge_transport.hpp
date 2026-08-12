#pragma once

#include "application/coordinator.hpp"
#include "transport/listener.hpp"

namespace dovahlink::application {

// The real TransportLifecycle: owns nothing itself, just holds references
// to the two loopback listeners (IPv4 and IPv6) constructed and bound
// before the coordinator is ever started -- LoopbackListener::Create binds
// and starts listening as part of construction, so there is no separate
// "start listening" step to defer into Start().
//
// This split matters given Coordinator::Start()'s fixed call order
// (RegisterAll, then WorkerPool::Start(), then TransportLifecycle::Start()):
// a worker pool accept-loop thread must be able to safely begin accepting
// the instant it starts, before this class's own Start() has necessarily
// run, which only works if the listeners are already listening by then.
//
// CancelCompletions is a no-op: this bridge's transport is fully
// synchronous (one blocking accept/read/write call at a time, per
// connection), so there are no in-flight async completions to cancel.
class BridgeTransport : public TransportLifecycle {
public:
    BridgeTransport(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6);

    void Start() override;
    void CancelCompletions() override;

    // Closes both listeners' acceptors. Safe to call even if
    // BridgeWorkerPool::Stop() already closed them to unblock its own
    // accept-loop threads (see that class's docs) -- closing an
    // already-closed acceptor is a harmless no-op.
    void Close() override;

private:
    transport::LoopbackListener& listenerV4_;
    transport::LoopbackListener& listenerV6_;
};

}  // namespace dovahlink::application
