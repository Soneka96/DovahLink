#pragma once

#include <chrono>

namespace dovahlink::application {

//  ---- Capture ----

///  Maximum capture period for a `RateClass::kFast` value: 200 milliseconds.
///  Rate classes are maximum capture frequencies, not publication or
///  network-send cadences (roadmap/04-live-state-synchronization-foundation.md).
///  A provisional scheduling hypothesis, subject to profiling rather than a
///  fixed protocol constant.
inline constexpr std::chrono::milliseconds kFastCapturePeriod{200};

///  Maximum capture period for a `RateClass::kMedium` value: 1 second.
inline constexpr std::chrono::seconds kMediumCapturePeriod{1};

///  Maximum capture period for a `RateClass::kSlow` value: 2 seconds.
inline constexpr std::chrono::seconds kSlowCapturePeriod{2};

} //  namespace dovahlink::application
