#include "application/event_coalescer.hpp"

#include <algorithm>
#include <utility>

namespace dovahlink::application {

EventCoalescer::EventCoalescer(OutboundQueue& queue) : queue_(queue) {}

bool EventCoalescer::PublishEvent(std::string stateArea, std::string message) {
    auto [state, inserted] = stateByArea_.try_emplace(std::move(stateArea), std::move(message));
    if (!inserted) {
        if (state->second.has_value()) {
            // The map node already owns the area key, so losing a dependent
            // assigned revision becomes recovery without another allocation.
            state->second.reset();
        }
        return false;
    }
    return true;
}

void EventCoalescer::Flush() {
    for (auto it = stateByArea_.begin(); it != stateByArea_.end();) {
        if (!it->second.has_value()) {
            ++it;
            continue;
        }

        EnqueueResult result = queue_.EnqueueEvent(*it->second);
        if (result != EnqueueResult::kEnqueued) {
            it->second.reset();
            ++it;
        } else {
            it = stateByArea_.erase(it);
        }
    }
}

bool EventCoalescer::NeedsRecovery(const std::string& stateArea) const {
    auto state = stateByArea_.find(stateArea);
    return state != stateByArea_.end() && !state->second.has_value();
}

void EventCoalescer::MarkRecovered(const std::string& stateArea) {
    auto state = stateByArea_.find(stateArea);
    if (state != stateByArea_.end() && !state->second.has_value()) {
        stateByArea_.erase(state);
    }
}

std::size_t EventCoalescer::PendingCount() const {
    return static_cast<std::size_t>(std::ranges::count_if(
        stateByArea_, [](const auto& state) { return state.second.has_value(); }));
}

}  // namespace dovahlink::application
