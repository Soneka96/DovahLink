#include "application/event_coalescer.hpp"

#include <utility>

namespace dovahlink::application {

/**
 * @brief Creates an event coalescer associated with an outbound queue.
 *
 * @param queue Queue used to enqueue pending events.
 */
EventCoalescer::EventCoalescer(OutboundQueue& queue) : queue_(queue) {}

/**
 * @brief Stores the latest pending event for a state area.
 *
 * @param stateArea State area associated with the event.
 * @param message Event message to store.
 * @return true if the event was stored, false if the state area requires recovery.
 */
bool EventCoalescer::PublishEvent(std::string stateArea, std::string message) {
    if (needsRecovery_.contains(stateArea)) {
        return false;
    }
    pendingByArea_[std::move(stateArea)] = std::move(message);
    return true;
}

/**
 * @brief Enqueues all pending events and marks areas with unsuccessful enqueues for recovery.
 */
void EventCoalescer::Flush() {
    for (auto it = pendingByArea_.begin(); it != pendingByArea_.end();) {
        EnqueueResult result = queue_.EnqueueEvent(it->second);
        if (result != EnqueueResult::kEnqueued) {
            needsRecovery_.insert(it->first);
        }
        it = pendingByArea_.erase(it);
    }
}

/**
 * @brief Determines whether a state area requires recovery.
 *
 * @param stateArea The state area to check.
 * @return `true` if the state area requires recovery, `false` otherwise.
 */
bool EventCoalescer::NeedsRecovery(const std::string& stateArea) const {
    return needsRecovery_.contains(stateArea);
}

/**
 * @brief Clears the recovery requirement for a state area.
 *
 * @param stateArea State area that has completed recovery.
 */
void EventCoalescer::MarkRecovered(const std::string& stateArea) {
    needsRecovery_.erase(stateArea);
}

/**
 * @brief Reports the number of state areas with pending events.
 *
 * @return std::size_t Number of pending state-area entries.
 */
std::size_t EventCoalescer::PendingCount() const {
    return pendingByArea_.size();
}

}  // namespace dovahlink::application
