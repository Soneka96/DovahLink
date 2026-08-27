#pragma once

#include "transport/loopback_listener.hpp"

#include <atomic>
#include <condition_variable>
#include <mutex>

namespace dovahlink::transport {

///  Stores the private synchronization state for one loopback listener's
///  asynchronous accept lifecycle.
struct LoopbackListener::LifecycleState {
    ///  Serializes lifecycle transitions and completion publication.
    std::mutex mutex;
    ///  Wakes Close() after the accept loop exits.
    std::condition_variable changed;
    ///  Indicates that one asynchronous accept operation is active.
    bool running{false};
    ///  Indicates that the acceptor's I/O context is still running.
    bool contextRunning{false};
    ///  Requests cancellation before or during an accept operation.
    bool closeRequested{false};
    ///  Prevents duplicate cancellation posts.
    bool closePosted{false};
    ///  Marks the current accept operation settled when Close() cancels it.
    std::shared_ptr<std::atomic_bool> acceptCompleted;
};

} //  namespace dovahlink::transport
