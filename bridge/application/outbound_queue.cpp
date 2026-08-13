#include "application/outbound_queue.hpp"

#include "security/limits.hpp"

#include <utility>

namespace dovahlink::application {

/**
 * @brief Enqueues a message in the control lane when capacity is available.
 *
 * @param message Control message to enqueue.
 * @return EnqueueResult `kEnqueued` when the message is added, or
 *     `kControlLaneFull` when the control lane has reached its capacity.
 */
EnqueueResult OutboundQueue::EnqueueControl(std::string message) {
    if (control_.size() >= security::kReservedControlRecoverySlots) {
        return EnqueueResult::kControlLaneFull;
    }
    control_.push_back(std::move(message));
    return EnqueueResult::kEnqueued;
}

/**
 * @brief Enqueues an event message when the event lane has capacity.
 *
 * @param message Event message to enqueue.
 * @return EnqueueResult `kEnqueued` if the message was added, or
 *         `kEventLaneFull` if the event lane has reached its capacity.
 */
EnqueueResult OutboundQueue::EnqueueEvent(std::string message) {
    if (events_.size() >= security::kReservedEventSlots) {
        return EnqueueResult::kEventLaneFull;
    }
    events_.push_back(std::move(message));
    return EnqueueResult::kEnqueued;
}

/**
 * @brief Removes and returns the oldest control message.
 *
 * @return The oldest control message, or `std::nullopt` if the control queue is empty.
 */
std::optional<std::string> OutboundQueue::DequeueControl() {
    if (control_.empty()) {
        return std::nullopt;
    }
    std::string message = std::move(control_.front());
    control_.pop_front();
    return message;
}

/**
 * @brief Removes and returns the oldest event message.
 *
 * @return The oldest event message, or `std::nullopt` if the event queue is empty.
 */
std::optional<std::string> OutboundQueue::DequeueEvent() {
    if (events_.empty()) {
        return std::nullopt;
    }
    std::string message = std::move(events_.front());
    events_.pop_front();
    return message;
}

/**
 * @brief Returns the number of messages in the control lane.
 *
 * @return std::size_t Current control-lane message count.
 */
std::size_t OutboundQueue::ControlLaneSize() const {
    return control_.size();
}

/**
 * @brief Returns the number of messages currently queued in the event lane.
 *
 * @return std::size_t Number of queued event messages.
 */
std::size_t OutboundQueue::EventLaneSize() const {
    return events_.size();
}

}  // namespace dovahlink::application
