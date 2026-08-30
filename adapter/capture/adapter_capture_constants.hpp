#pragma once

#include <cstddef>

namespace dovahlink::adapter::capture {

//  ---- Handoff queue ----

///  The bounded capacity of the adapter's capture handoff queue. A game-thread
///  callback that would exceed this capacity is rejected rather than waited
///  for, per `ai/context/adapter/architecture.md`'s bounded, non-blocking
///  handoff requirement.
inline constexpr std::size_t kMaxAdapterCaptureQueueItems = 64;

} //  namespace dovahlink::adapter::capture
