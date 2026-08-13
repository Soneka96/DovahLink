#pragma once

#include <cstddef>
#include <deque>
#include <optional>
#include <string>

namespace dovahlink::application {

/// Describes the result of an outbound enqueue attempt.
enum class EnqueueResult {
    /// The message was accepted into its lane.
    kEnqueued,

    /// The reserved control/recovery lane was full.
    kControlLaneFull,

    /// The event lane was full.
    kEventLaneFull,
};

/// Bounded FIFO queues for control/recovery messages and state events.
/// The control lane reserves 16 messages and the event lane provides 112 messages.
/// One connection owns and drains each instance; it is not thread-safe.
class OutboundQueue {
public:
    /// Creates an empty outbound queue.
    OutboundQueue() = default;

    /// Adds a message to the reserved control lane when capacity permits.
    /// @param message Serialized control or recovery message.
    /// @return Enqueue result.
    [[nodiscard]] EnqueueResult EnqueueControl(std::string message);

    /// Adds a message to the event lane when capacity permits.
    /// @param message Serialized state-event message.
    /// @return Enqueue result.
    [[nodiscard]] EnqueueResult EnqueueEvent(std::string message);

    /// Removes the oldest control-lane message.
    /// @return Message, or no value when the lane is empty.
    [[nodiscard]] std::optional<std::string> DequeueControl();

    /// Removes the oldest event-lane message.
    /// @return Message, or no value when the lane is empty.
    [[nodiscard]] std::optional<std::string> DequeueEvent();

    /// Reports the number of queued control messages.
    [[nodiscard]] std::size_t ControlLaneSize() const;

    /// Reports the number of queued event messages.
    [[nodiscard]] std::size_t EventLaneSize() const;

private:
    /// FIFO storage for control and recovery messages.
    std::deque<std::string> control_;

    /// FIFO storage for state events.
    std::deque<std::string> events_;
};

}  // namespace dovahlink::application
