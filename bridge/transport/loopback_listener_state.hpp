#pragma once

#include "transport/loopback_listener.hpp"

#include <condition_variable>
#include <exception>
#include <mutex>

namespace dovahlink::transport {

///  Stores the private synchronization state for one loopback listener's
///  asynchronous accept lifecycle.
struct LoopbackListener::LifecycleState {
    ///  Serializes lifecycle transitions and completion publication.
    std::mutex mutex;
    ///  Wakes Close() after the accept loop exits.
    std::condition_variable changed;
    ///  Prevents more than one accept loop from owning the listener.
    bool started{false};
    ///  Indicates that the accept loop has not completed yet.
    bool running{false};
    ///  Requests cancellation before or during accept-loop startup.
    bool closeRequested{false};
    ///  Prevents duplicate cancellation posts.
    bool closePosted{false};
    ///  Transfers callback failures back through RunAcceptLoop().
    std::exception_ptr failure;
};

} //  namespace dovahlink::transport
