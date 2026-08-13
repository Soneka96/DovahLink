#include "application/event_coalescer.hpp"

#include <utility>

namespace dovahlink::application {

EventCoalescer::EventCoalescer(OutboundQueue& queue) : queue_(queue) {}

bool EventCoalescer::PublishEvent(std::string stateArea, std::string message) {
    if (needsRecovery_.contains(stateArea)) {
        return false;
    }
    pendingByArea_[std::move(stateArea)] = std::move(message);
    return true;
}

void EventCoalescer::Flush() {
    for (auto it = pendingByArea_.begin(); it != pendingByArea_.end();) {
        EnqueueResult result = queue_.EnqueueEvent(it->second);
        if (result != EnqueueResult::kEnqueued) {
            needsRecovery_.insert(it->first);
        }
        it = pendingByArea_.erase(it);
    }
}

bool EventCoalescer::NeedsRecovery(const std::string& stateArea) const {
    return needsRecovery_.contains(stateArea);
}

void EventCoalescer::MarkRecovered(const std::string& stateArea) {
    needsRecovery_.erase(stateArea);
}

std::size_t EventCoalescer::PendingCount() const {
    return pendingByArea_.size();
}

}  // namespace dovahlink::application
