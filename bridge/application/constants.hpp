#pragma once

#include <chrono>
#include <cstddef>

namespace dovahlink::application {

//  ---- Capture ----

///  Maximum capture period for a `RateClass::kFast` value: 200 milliseconds.
///  Rate classes are maximum capture frequencies, not publication or network-send cadences.
///  A provisional scheduling hypothesis, subject to profiling rather than a
///  fixed protocol constant.
inline constexpr std::chrono::milliseconds kFastCapturePeriod{200};

///  Maximum capture period for a `RateClass::kMedium` value: 1 second.
inline constexpr std::chrono::seconds kMediumCapturePeriod{1};

///  Maximum capture period for a `RateClass::kSlow` value: 2 seconds.
inline constexpr std::chrono::seconds kSlowCapturePeriod{2};

//  ---- Registered state areas ----

///  Maximum number of state-area keys `RegisteredStateAreaPolicy` accepts.
///  Sized for a small number of near-term production character domains with
///  modest headroom (`ai/context/protocol/security.md`'s "Input limits");
///  raising it later requires only a constant change, not a protocol change.
inline constexpr std::size_t kMaxRegisteredStateAreas = 8;

//  ---- Capture dispatch ----

///  Maximum number of `CaptureWorkItem` values `CaptureDispatchWorker` holds
///  awaiting processing. Comfortably exceeds one tick's worth of due keys
///  across every registered sampled key plus native-event bursts, without
///  growing unbounded when the worker falls behind; `TryEnqueue` fails
///  closed rather than growing past it, matching `ai/context/skse/
///  architecture.md`'s "never block the game thread."
inline constexpr std::size_t kMaxCaptureQueueItems = 64;

//  ---- Cadence tick ----

///  Interval at which `CadenceTickDriver`'s background thread attempts to
///  marshal a due-key check onto the game thread. Finer than
///  `kFastCapturePeriod` (200 ms), but the SKSE task queue controls when the
///  marshaled check actually runs; a delayed check skips missed instants rather
///  than producing a catch-up burst.
inline constexpr std::chrono::milliseconds kCadenceTickInterval{100};

} //  namespace dovahlink::application
