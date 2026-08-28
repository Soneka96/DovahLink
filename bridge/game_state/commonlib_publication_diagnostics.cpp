#include "SKSE/SKSE.h"

#include "game_state/commonlib_publication_diagnostics.hpp"

namespace dovahlink::game_state {

namespace {

///  Formats `reason` for diagnostic logging.
std::string_view DisconnectReasonName(application::DisconnectReason reason) {
    switch (reason) {
    case application::DisconnectReason::kReservedLaneFull:
        return "reserved_lane_full";
    case application::DisconnectReason::kEventOverflow:
        return "event_overflow";
    case application::DisconnectReason::kSendFailed:
        return "send_failed";
    }
    //  Unreachable: every enumerator is handled above.
    return "unknown";
}

} //  namespace

void CommonLibPublicationDiagnostics::RecordQueueDepth(
    std::size_t normalSlotsUsed, std::size_t heavySlotsUsed,
    std::size_t reservedSlotsUsed, std::size_t totalBytesUsed) {
    SKSE::log::info(
        "[publication] queue_depth normal={} heavy={} reserved={} bytes={}",
        normalSlotsUsed, heavySlotsUsed, reservedSlotsUsed, totalBytesUsed);
}

void CommonLibPublicationDiagnostics::RecordCoalesced(
    std::string_view stateArea) {
    SKSE::log::info("[publication] coalesced state_area={}", stateArea);
}

void CommonLibPublicationDiagnostics::RecordEnqueueLatency(
    std::chrono::steady_clock::duration latency) {
    SKSE::log::info(
        "[publication] enqueue_latency_us={}",
        std::chrono::duration_cast<std::chrono::microseconds>(latency).count());
}

void CommonLibPublicationDiagnostics::RecordDequeueLatency(
    std::chrono::steady_clock::duration latency) {
    SKSE::log::info(
        "[publication] dequeue_latency_us={}",
        std::chrono::duration_cast<std::chrono::microseconds>(latency).count());
}

void CommonLibPublicationDiagnostics::RecordRecovery(
    std::string_view stateArea, std::int64_t revision,
    std::size_t supersededEvents) {
    SKSE::log::info(
        "[publication] recovery state_area={} revision={} superseded={}",
        stateArea, revision, supersededEvents);
}

void CommonLibPublicationDiagnostics::RecordDisconnect(
    application::DisconnectReason reason) {
    SKSE::log::info("[publication] disconnect reason={}",
                    DisconnectReasonName(reason));
}

} //  namespace dovahlink::game_state
