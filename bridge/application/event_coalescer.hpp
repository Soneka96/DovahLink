#pragma once

#include "application/outbound_queue.hpp"

#include <cstddef>
#include <string>
#include <unordered_map>
#include <unordered_set>

namespace dovahlink::application {

// Coalesces outbound state_event messages per state area before they reach
// the bounded OutboundQueue's event lane: PublishEvent replaces any
// not-yet-flushed prior event for the same state area rather than queueing
// both, so a burst of updates collapses to just the latest before delivery
// is attempted (the v1 queue policy in ai/context/skse/architecture.md:
// "intermediate updates may be dropped").
//
// If a coalesced event still cannot fit in the event lane once Flush is
// called, the connection cannot keep up even with coalescing: that state
// area is marked as needing recovery and the event is dropped, never
// retried. The next client-visible state for that area must come from a
// fresh snapshot delivered through the control/recovery lane, not another
// event -- see MarkRecovered. Not thread-safe: intended to be owned by one
// connection's application-side handling.
class EventCoalescer {
public:
    explicit EventCoalescer(OutboundQueue& queue);

    // Records the latest event for `stateArea`, replacing any pending event
    // for the same area not yet delivered by Flush. Returns false and drops
    // `message` without staging it while `stateArea` needs recovery (see
    // NeedsRecovery): an event published before a fresh baseline is
    // re-established is meaningless. Returns true once staged.
    bool PublishEvent(std::string stateArea, std::string message);

    // Attempts to enqueue every pending coalesced event into the queue's
    // event lane. Each attempt either succeeds (delivered, removed from
    // pending) or fails and is dropped, marking its state area as needing
    // recovery -- never left pending for a later retry.
    void Flush();

    // True if `stateArea` needs a fresh snapshot before its next event may
    // be published or trusted as current.
    [[nodiscard]] bool NeedsRecovery(const std::string& stateArea) const;

    // Call once a fresh snapshot for `stateArea` has been captured and
    // published through the recovery lane, clearing its recovery mark so
    // PublishEvent accepts updates for it again.
    void MarkRecovered(const std::string& stateArea);

    // The number of state areas with an event currently staged, not yet
    // delivered by Flush.
    [[nodiscard]] std::size_t PendingCount() const;

private:
    OutboundQueue& queue_;
    std::unordered_map<std::string, std::string> pendingByArea_;
    std::unordered_set<std::string> needsRecovery_;
};

}  // namespace dovahlink::application
