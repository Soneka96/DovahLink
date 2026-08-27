#pragma once

#include "transport/loopback_listener.hpp"

#include <boost/asio/executor_work_guard.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <atomic>
#include <future>
#include <thread>

namespace dovahlink::transport {

///  Owns the private asynchronous resources behind one loopback listener: its
///  `io_context`, its bound acceptor, and the dedicated background thread
///  that keeps that `io_context` running for the object's whole life. Held by
///  `LoopbackListener` through a `shared_ptr` so the listener handle itself
///  stays cheaply movable (a plain pointer copy) while every callback posted
///  onto `ioContext` keeps a stable address to reference regardless of where
///  the owning `LoopbackListener` handle is moved to.
struct LoopbackListener::OwnerState {
    ///  Default-constructs an unopened acceptor bound to a freshly owned
    ///  `io_context`; `LoopbackListener::Create` opens, binds, and listens on
    ///  it before starting `ownerThread`.
    OwnerState();

    ///  Drives `acceptor`'s asynchronous operations for this object's whole
    ///  life. Only `ownerThread` touches `acceptor` once it starts running;
    ///  `LoopbackListener::Create` may still touch it synchronously before
    ///  that, while no thread has started yet.
    boost::asio::io_context ioContext;

    ///  Keeps `ioContext.run()` from returning while idle between accepts;
    ///  reset only by `Join()`, once, after the acceptor has actually closed.
    boost::asio::executor_work_guard<boost::asio::io_context::executor_type>
        workGuard;

    ///  The bound, listening acceptor.
    boost::asio::ip::tcp::acceptor acceptor;

    ///  Runs `ioContext.run()` for the object's whole life, started once
    ///  `acceptor` is successfully listening.
    std::thread ownerThread;

    ///  Rejects a concurrent `AcceptLoopbackOnly()` call while one is already
    ///  in flight; cleared once that call's outcome is known.
    std::atomic_bool acceptInFlight{false};

    ///  Set by the first `Close()` call; later calls are no-ops.
    std::atomic_bool closeRequested{false};

    ///  Fulfilled once `ownerThread` has actually run the cancel-and-close job
    ///  `Close()` posts, regardless of which `Close()` call posted it. `Join()`
    ///  waits on this before stopping `ioContext`, so a queued close is never
    ///  abandoned mid-flight.
    std::promise<void> closed;

    ///  Set once `Join()` has stopped `ioContext` and joined `ownerThread`;
    ///  later calls are no-ops.
    std::atomic_bool joined{false};
};

} //  namespace dovahlink::transport
