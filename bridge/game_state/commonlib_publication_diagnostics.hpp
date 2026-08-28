#pragma once

#include "application/publication_diagnostics.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace dovahlink::game_state {

///  Reports `application::IPublicationDiagnostics` signals through
///  `SKSE::log`. Declared in the CommonLib-linked game-state target because
///  its consumer, `application::BoundedOutboundQueue`, lives in the
///  Skyrim-independent core and cannot itself link SKSE/CommonLib
///  (`application/publication_diagnostics.hpp` documents this split).
class CommonLibPublicationDiagnostics final
    : public application::IPublicationDiagnostics {
  public:
    ///  @copydoc application::IPublicationDiagnostics::RecordQueueDepth
    void RecordQueueDepth(std::size_t normalSlotsUsed,
                          std::size_t heavySlotsUsed,
                          std::size_t reservedSlotsUsed,
                          std::size_t totalBytesUsed) override;

    ///  @copydoc application::IPublicationDiagnostics::RecordCoalesced
    void RecordCoalesced(std::string_view stateArea) override;

    ///  @copydoc application::IPublicationDiagnostics::RecordEnqueueLatency
    void RecordEnqueueLatency(
        std::chrono::steady_clock::duration latency) override;

    ///  @copydoc application::IPublicationDiagnostics::RecordDequeueLatency
    void RecordDequeueLatency(
        std::chrono::steady_clock::duration latency) override;

    ///  @copydoc application::IPublicationDiagnostics::RecordRecovery
    void RecordRecovery(std::string_view stateArea, std::int64_t revision,
                        std::size_t supersededEvents) override;

    ///  @copydoc application::IPublicationDiagnostics::RecordDisconnect
    void RecordDisconnect(application::DisconnectReason reason) override;
};

} //  namespace dovahlink::game_state
