#pragma once

#include <chrono>
#include <cstddef>
#include <deque>
#include <mutex>

namespace dovahlink::security {

///  Narrow capability for recording events and counting how many remain
///  active within a sliding time window.
class IRateWindowCounter {
  public:
    ///  Releases the interface without performing work.
    virtual ~IRateWindowCounter() = default;

    ///  Records an event and returns the active event count including it.
    [[nodiscard]] virtual std::size_t
    RecordEvent(std::chrono::steady_clock::time_point now) = 0;

    ///  Returns the active event count without recording a new event.
    [[nodiscard]] virtual std::size_t
    ActiveCount(std::chrono::steady_clock::time_point now) = 0;
};

///  Thread-safe sliding-window counter with caller-supplied timestamps.
class RateWindowCounter : public IRateWindowCounter {
  public:
    ///  Creates a counter whose recorded events remain active for `window`.
    explicit RateWindowCounter(std::chrono::steady_clock::duration window);

    ///  @copydoc IRateWindowCounter::RecordEvent
    [[nodiscard]] std::size_t
    RecordEvent(std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IRateWindowCounter::ActiveCount
    [[nodiscard]] std::size_t
    ActiveCount(std::chrono::steady_clock::time_point now) override;

  private:
    ///  Removes timestamps outside the active window while locked.
    void PruneLocked(std::chrono::steady_clock::time_point now);

    ///  Serializes access to the counter state.
    std::mutex mutex_;
    ///  Duration for which an event remains in the active window.
    std::chrono::steady_clock::duration window_;
    ///  Recorded event timestamps, including entries not yet pruned.
    std::deque<std::chrono::steady_clock::time_point> eventTimes_;
};

} //  namespace dovahlink::security
