#pragma once

#include "shared/enums.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace dovahlink::application {

///  Receives observability signals from `BoundedOutboundQueue` for the live
///  publication pipeline's queue depth, coalescing, enqueue/dequeue latency,
///  recovery, and disconnect behavior (`ai/context/skse/architecture.md`'s
///  "Failure semantics"). Declared here, CommonLib-free, because its consumer
///  (`BoundedOutboundQueue`) lives in the Skyrim-independent core; its one
///  concrete implementation, `CommonLibPublicationDiagnostics`
///  (`game_state/commonlib_publication_diagnostics.hpp`), can only be
///  compiled into a target linked against CommonLibSSE because it logs
///  through `SKSE::log`. This is the same CMake-target dependency-wall split
///  documented in `ai/context/skse/cpp-style.md` for `IBridgeCallbackRegistry`
///  and `IPairingNotificationSink`.
class IPublicationDiagnostics {
  public:
    ///  Allows destruction through the interface.
    virtual ~IPublicationDiagnostics() = default;

    ///  Reports current data- and reserved-lane occupancy immediately after
    ///  an admission, replacement, or removal changes it.
    ///  @param normalSlotsUsed Normal data-lane slots currently occupied.
    ///  @param heavySlotsUsed Heavy data-lane slots currently occupied.
    ///  @param reservedSlotsUsed Reserved control/recovery slots currently
    ///  occupied.
    ///  @param totalBytesUsed Total encoded bytes currently occupying the
    ///  data lanes.
    virtual void RecordQueueDepth(std::size_t normalSlotsUsed,
                                  std::size_t heavySlotsUsed,
                                  std::size_t reservedSlotsUsed,
                                  std::size_t totalBytesUsed) = 0;

    ///  Reports that a pending Snapshot slot was replaced in place rather
    ///  than admitted as a new entry.
    ///  @param stateArea State area whose pending slot was replaced.
    virtual void RecordCoalesced(std::string_view stateArea) = 0;

    ///  Reports the time an accepted publication spent from submission until
    ///  its enqueue decision completed. The decision may admit a new entry,
    ///  replace a pending Snapshot, retain a bounded dirty marker, discard an
    ///  obsolete Event, or stop the session for an overflow.
    ///  @param latency Elapsed time between publication submission and the
    ///  completed enqueue decision.
    virtual void RecordEnqueueLatency(
        std::chrono::steady_clock::duration latency) = 0;

    ///  Reports the time one data- or reserved-lane entry spent admitted in
    ///  the queue before its delivery completed.
    ///  @param latency Elapsed time between admission and delivery
    ///  completion.
    virtual void RecordDequeueLatency(
        std::chrono::steady_clock::duration latency) = 0;

    ///  Reports that a recovery Snapshot established a new per-state-area
    ///  barrier.
    ///  @param stateArea State area the barrier was established for.
    ///  @param revision Revision the barrier was established at.
    ///  @param supersededEvents Number of already-queued Events for this
    ///  state area discarded as superseded by the new barrier.
    virtual void RecordRecovery(std::string_view stateArea,
                                std::int64_t revision,
                                std::size_t supersededEvents) = 0;

    ///  Reports that the session was disconnected and why.
    ///  @param reason Cause of the disconnect.
    virtual void RecordDisconnect(DisconnectReason reason) = 0;
};

} //  namespace dovahlink::application
