#pragma once

#include <atomic>

namespace dovahlink::transport {

// Enforces the Phase 1 one-connected-client limit at the transport boundary
// (ai/context/protocol/security.md: "maximum connected clients during the
// first proof: 1"). One instance shared across every accepted connection
// attempt; the accept loop (a later step) must call TryAcquire before
// proceeding to a WebSocket handshake for a newly accepted socket, and
// close the socket immediately without a handshake if it fails.
class ConnectionSlot {
public:
    ConnectionSlot() = default;

    // Attempts to claim the single connection slot. Returns false if it is
    // already occupied. Thread-safe: a concurrent race for the slot has
    // exactly one winner.
    [[nodiscard]] bool TryAcquire();

    // Releases the slot. Safe to call even if it was never acquired (a no-op
    // in that case).
    void Release();

    [[nodiscard]] bool IsOccupied() const;

private:
    std::atomic<bool> occupied_{false};
};

}  // namespace dovahlink::transport
