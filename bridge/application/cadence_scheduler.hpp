#pragma once

#include "shared/enums.hpp"

#include <chrono>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace dovahlink::application {

///  Computes due instants for recurring sampled capture, per one shared,
///  bounded sampler (`ai/context/skse/architecture.md`'s "Threading and
///  callbacks"). Rate classes are maximum capture frequencies, not
///  publication or network-send cadences. Due instants are aligned to
///  `std::chrono::steady_clock::time_point{}` plus each key's stagger
///  offset, not to construction time.
class ICadenceScheduler {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICadenceScheduler() = default;

    ///  Registers a sampled key's cadence, replacing any cadence previously
    ///  registered for the same key. The key's first due instant is its
    ///  stagger offset.
    ///  @param key Canonical identifier for the captured value.
    ///  @param rateClass Maximum capture frequency.
    ///  @param stagger Offset applied to every due instant for this key, so
    ///  callers may spread same-rate-class keys across their shared period
    ///  instead of capturing them in the same instant.
    virtual void RegisterSampled(std::string key, RateClass rateClass,
                                 std::chrono::steady_clock::duration stagger) = 0;

    ///  Reports every registered key due at or before `now`, and advances
    ///  each reported key's next due instant to the next aligned instant
    ///  strictly after `now`. Every due instant strictly between the key's
    ///  previous due instant and `now` is skipped rather than reported,
    ///  so a late call never produces a catch-up burst.
    ///  @param now Current time.
    ///  @return Keys due at or before `now`, in no particular order.
    [[nodiscard]] virtual std::vector<std::string>
    DueKeys(std::chrono::steady_clock::time_point now) = 0;
};

///  @copydoc ICadenceScheduler
class CadenceScheduler final : public ICadenceScheduler {
  public:
    ///  @copydoc ICadenceScheduler::RegisterSampled
    void RegisterSampled(std::string key, RateClass rateClass,
                         std::chrono::steady_clock::duration stagger) override;

    ///  @copydoc ICadenceScheduler::DueKeys
    [[nodiscard]] std::vector<std::string>
    DueKeys(std::chrono::steady_clock::time_point now) override;

  private:
    ///  One key's cadence and next due instant.
    struct Schedule {
        ///  Maximum capture period for this key.
        std::chrono::steady_clock::duration period;

        ///  Next instant at which this key becomes due.
        std::chrono::steady_clock::time_point nextDue;
    };

    ///  Synchronizes access to `schedules_`.
    std::mutex mutex_;

    ///  Registered cadence per sampled key.
    std::unordered_map<std::string, Schedule> schedules_;
};

} //  namespace dovahlink::application
