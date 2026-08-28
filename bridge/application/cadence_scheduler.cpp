#include "application/cadence_scheduler.hpp"

#include "application/constants.hpp"

#include <utility>

namespace dovahlink::application {

namespace {

///  Returns the maximum capture period for a rate class.
///  @param rateClass Rate class to resolve.
std::chrono::steady_clock::duration PeriodFor(RateClass rateClass) {
    switch (rateClass) {
    case RateClass::kFast:
        return kFastCapturePeriod;
    case RateClass::kMedium:
        return kMediumCapturePeriod;
    case RateClass::kSlow:
        return kSlowCapturePeriod;
    }
    std::unreachable();
}

} //  namespace

void CadenceScheduler::RegisterSampled(
    std::string key, RateClass rateClass,
    std::chrono::steady_clock::duration stagger) {
    std::lock_guard<std::mutex> lock(mutex_);
    schedules_.insert_or_assign(
        std::move(key),
        Schedule{.period = PeriodFor(rateClass),
                 .nextDue = std::chrono::steady_clock::time_point{} + stagger});
}

std::vector<std::string>
CadenceScheduler::DueKeys(std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<std::string> due;
    for (auto& [key, schedule] : schedules_) {
        if (schedule.nextDue > now) {
            continue;
        }
        due.push_back(key);
        auto elapsedTicks = (now - schedule.nextDue) / schedule.period + 1;
        schedule.nextDue += elapsedTicks * schedule.period;
    }
    return due;
}

} //  namespace dovahlink::application
