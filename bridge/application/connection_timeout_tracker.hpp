#pragma once

#include <chrono>

namespace dovahlink::application {

// Tracks the two connection-lifecycle timeouts from
// ai/context/protocol/security.md: a 5-second handshake timeout before
// authentication completes, and a 60-second idle timeout afterward (no valid
// message or heartbeat). One instance per connection.
//
// The caller supplies the current time explicitly on every call rather than
// this type reading a clock itself, so tests can drive it deterministically
// without real time passing (ai/context/skse/testing.md).
class ConnectionTimeoutTracker {
public:
    // `now` is the time the connection was accepted; the handshake deadline
    // starts counting from here.
    explicit ConnectionTimeoutTracker(std::chrono::steady_clock::time_point now);

    // Call once authentication succeeds (hello_ack sent). Switches from
    // tracking the handshake timeout to tracking the idle timeout, with the
    // idle deadline starting from `now`. A no-op if already authenticated,
    // or if `now` has already reached the current deadline: a connection
    // that has timed out cannot be revived by authenticating late, and the
    // caller is expected to close it instead.
    void MarkAuthenticated(std::chrono::steady_clock::time_point now);

    // Call whenever a valid inbound message or heartbeat arrives on an
    // authenticated connection. Resets the idle deadline forward from `now`.
    // Has no effect before authentication (the handshake deadline is fixed),
    // or once `now` has already reached the current deadline: a timed-out
    // connection cannot be revived by a late message.
    void RecordActivity(std::chrono::steady_clock::time_point now);

    // True once the connection has exceeded whichever timeout currently
    // applies: the handshake deadline before authentication, or the idle
    // deadline after.
    [[nodiscard]] bool IsTimedOut(std::chrono::steady_clock::time_point now) const;

private:
    bool authenticated_ = false;
    std::chrono::steady_clock::time_point deadline_;
};

}  // namespace dovahlink::application
