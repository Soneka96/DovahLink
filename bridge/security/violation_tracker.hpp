#pragma once

#include "security/rate_window_counter.hpp"

#include <chrono>

namespace dovahlink::security {

///  Narrow capability for pacing per-connection protocol violations.
class IViolationTracker {
  public:
    ///  Releases the interface without performing work.
    virtual ~IViolationTracker() = default;

    ///  Records a violation and reports whether the connection must close.
    [[nodiscard]] virtual bool
    RecordViolationAndCheckLimit(std::chrono::steady_clock::time_point now) = 0;
};

///  Per-connection protocol-violation tracker.
class ViolationTracker : public IViolationTracker {
  public:
    ///  Creates a tracker using the configured violation window.
    ViolationTracker();

    ///  @copydoc IViolationTracker::RecordViolationAndCheckLimit
    [[nodiscard]] bool RecordViolationAndCheckLimit(
        std::chrono::steady_clock::time_point now) override;

  private:
    ///  Counter tracking violations for one connection.
    RateWindowCounter counter_;
};

} //  namespace dovahlink::security
