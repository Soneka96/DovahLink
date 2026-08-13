#pragma once

#include "application/outbound_queue.hpp"

#include <cstddef>
#include <string>
#include <unordered_map>
#include <unordered_set>

namespace dovahlink::application {

/// Coalesces pending state events before they enter the bounded event lane.
/// A failed enqueue drops the event and requires a fresh snapshot before publication resumes.
/// One instance belongs to one connection and is not thread-safe.
class EventCoalescer {
public:
    /// Uses `queue` for event delivery.
    /// @param queue Outbound queue owned by the connection.
    explicit EventCoalescer(OutboundQueue& queue);

    /// Replaces the pending event for a state area.
    /// @param stateArea Canonical state-area identifier.
    /// @param message Serialized event message.
    /// @return `false` when recovery is required and the event is rejected.
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

    /// Latest pending event for each state area.
    std::unordered_map<std::string, std::string> pendingByArea_;

    /// State areas awaiting snapshot recovery.
    std::unordered_set<std::string> needsRecovery_;
};

}  // namespace dovahlink::application
