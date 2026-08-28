#pragma once

#include <chrono>

namespace dovahlink::application {

//  ---- Capture ----

///  Maximum capture period for a `RateClass::kFast` value. Rate classes are
///  maximum capture frequencies, not publication or network-send cadences
///  (roadmap/04-live-state-synchronization-foundation.md). A provisional
///  scheduling hypothesis, subject to profiling rather than a fixed protocol
///  constant.
inline constexpr std::chrono::seconds kFastCapturePeriod{1};

///  Maximum capture period for a `RateClass::kMedium` value.
inline constexpr std::chrono::seconds kMediumCapturePeriod{2};

///  Maximum capture period for a `RateClass::kSlow` value.
inline constexpr std::chrono::seconds kSlowCapturePeriod{3};

} //  namespace dovahlink::application
