#include "game_state/commonlib_capture_queue_diagnostics.hpp"

#include "SKSE/SKSE.h"

namespace dovahlink::game_state {

namespace {

///  Formats `mode` for diagnostic logging.
std::string_view CaptureModeName(application::CaptureMode mode) {
    switch (mode) {
    case application::CaptureMode::kSnapshot:
        return "snapshot";
    case application::CaptureMode::kEvent:
        return "event";
    }
    //  Unreachable: every enumerator is handled above.
    return "unknown";
}

} //  namespace

void CommonLibCaptureQueueDiagnostics::RecordCaptureRejected(
    std::string_view stateArea, application::CaptureMode mode) noexcept {
    try {
        //  A Snapshot rejection is recoverable (the next sample tick
        //  re-captures current state), so it is a routine warning. An Event
        //  rejection is a state transition nothing will re-capture later,
        //  so it is logged as an error: this loss is diagnosed, not
        //  prevented or recovered.
        if (mode == application::CaptureMode::kEvent) {
            SKSE::log::error("[capture] queue_full state_area={} mode={}",
                             stateArea, CaptureModeName(mode));
        } else {
            SKSE::log::warn("[capture] queue_full state_area={} mode={}",
                            stateArea, CaptureModeName(mode));
        }
    } catch (...) {
        //  This method is called from a game-thread capture callback and
        //  must never let a logging failure escape as an exception.
    }
}

} //  namespace dovahlink::game_state
