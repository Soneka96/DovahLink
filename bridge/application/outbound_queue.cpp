#include "application/outbound_queue.hpp"

#include "security/limits.hpp"

#include <utility>

namespace dovahlink::application {

EnqueueResult OutboundQueue::EnqueueControl(std::string message) {
    if (control_.size() >= security::kReservedControlRecoverySlots) {
        return EnqueueResult::kControlLaneFull;
    }
    control_.push_back(std::move(message));
    return EnqueueResult::kEnqueued;
}

EnqueueResult OutboundQueue::EnqueueEvent(std::string message) {
    if (events_.size() >= security::kReservedEventSlots) {
        return EnqueueResult::kEventLaneFull;
    }
    events_.push_back(std::move(message));
    return EnqueueResult::kEnqueued;
}

std::optional<std::string> OutboundQueue::DequeueControl() {
    if (control_.empty()) {
        return std::nullopt;
    }
    std::string message = std::move(control_.front());
    control_.pop_front();
    return message;
}

std::optional<std::string> OutboundQueue::DequeueEvent() {
    if (events_.empty()) {
        return std::nullopt;
    }
    std::string message = std::move(events_.front());
    events_.pop_front();
    return message;
}

std::size_t OutboundQueue::ControlLaneSize() const {
    return control_.size();
}

std::size_t OutboundQueue::EventLaneSize() const {
    return events_.size();
}

}  // namespace dovahlink::application
