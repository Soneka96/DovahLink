#pragma once

#include <cstddef>
#include <deque>
#include <optional>
#include <string>

namespace dovahlink::application {

// The result of attempting to enqueue an outbound message.
enum class EnqueueResult {
    kEnqueued,           // accepted into the lane.
    kControlLaneFull,    // the control/recovery lane is full; this message was
                         // NOT enqueued. The caller must mark the client
                         // unavailable and close the connection
                         // (ai/context/skse/architecture.md).
    kEventLaneFull,      // the event lane is full; this message was NOT
                         // enqueued. Step 19 replaces this plain-reject
                         // behavior with latest-state coalescing.
};

// The bounded outbound queue split into a reserved control/recovery lane and
// an event lane (ai/context/skse/architecture.md, bridge/security/limits.hpp).
// Snapshots, acknowledgements, errors, and recovery messages use the control
// lane and are never silently dropped; state events use the event lane. Each
// lane is a plain FIFO of opaque, already-serialized message text; this type
// does not interpret message content. Not thread-safe: intended to be owned
// and drained by one connection's transport-side worker.
class OutboundQueue {
public:
    OutboundQueue() = default;

    [[nodiscard]] EnqueueResult EnqueueControl(std::string message);
    [[nodiscard]] EnqueueResult EnqueueEvent(std::string message);

    // Removes and returns the next control-lane message in FIFO order, or
    // nullopt if the control lane is empty.
    [[nodiscard]] std::optional<std::string> DequeueControl();

    // Removes and returns the next event-lane message in FIFO order, or
    // nullopt if the event lane is empty.
    [[nodiscard]] std::optional<std::string> DequeueEvent();

    [[nodiscard]] std::size_t ControlLaneSize() const;
    [[nodiscard]] std::size_t EventLaneSize() const;

private:
    std::deque<std::string> control_;
    std::deque<std::string> events_;
};

}  // namespace dovahlink::application
