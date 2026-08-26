#include "security/rate_window_counter.hpp"

namespace dovahlink::security {

RateWindowCounter::RateWindowCounter(std::chrono::steady_clock::duration window)
    : window_(window) {}

std::size_t
RateWindowCounter::RecordEvent(std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);

    PruneLocked(now);
    eventTimes_.push_back(now);
    return eventTimes_.size();
}

std::size_t
RateWindowCounter::ActiveCount(std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    PruneLocked(now);
    return eventTimes_.size();
}

void RateWindowCounter::PruneLocked(std::chrono::steady_clock::time_point now) {

    //  Concurrent callers each read steady_clock::now() independently before
    //  acquiring the lock, so insertion order is not guaranteed to match
    //  timestamp order; erase every stale entry rather than assuming the
    //  oldest is always at the front.
    std::erase_if(eventTimes_, [&](std::chrono::steady_clock::time_point t) {
        return t <= now - window_;
    });
}

} //  namespace dovahlink::security
