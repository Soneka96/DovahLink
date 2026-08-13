#pragma once

#include "application/outbound_queue.hpp"

#include <cstddef>
#include <optional>
#include <string>
#include <unordered_map>

namespace dovahlink::application {

/// Stages assigned state events before they enter the bounded event lane.
/// Replacement or a failed enqueue drops the staged event and requires a fresh
/// snapshot before publication resumes. State changes must be coalesced before
/// revision metadata is assigned and the resulting event is published here.
/// One instance belongs to one connection and is not thread-safe.
class EventCoalescer {
public:
    /// Uses `queue` for event delivery.
    /// @param queue Outbound queue owned by the connection.
    explicit EventCoalescer(OutboundQueue& queue);

    /// Stages one assigned event for a state area.
    /// @param stateArea Canonical state-area identifier.
    /// @param message Serialized event message.
    /// @return `false` when recovery is required, including an attempted replacement.
    bool PublishEvent(std::string stateArea, std::string message);

    /// Attempts to enqueue every pending event and marks failed areas for recovery.
    void Flush();

    /// Reports whether a state area needs a fresh snapshot.
    /// @param stateArea Canonical state-area identifier.
    /// @return `true` when recovery is required.
    [[nodiscard]] bool NeedsRecovery(const std::string& stateArea) const;

    /// Clears the recovery requirement for a state area.
    /// @param stateArea Canonical state-area identifier.
    void MarkRecovered(const std::string& stateArea);

    /// Reports the number of pending state-area events.
    [[nodiscard]] std::size_t PendingCount() const;

private:
    /// Outbound queue receiving flushed events.
    OutboundQueue& queue_;

    /// Per-area pending message, or no message while snapshot recovery is required.
    std::unordered_map<std::string, std::optional<std::string>> stateByArea_;
};

}  // namespace dovahlink::application
