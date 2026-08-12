#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"

#include <chrono>
#include <string>
#include <vector>

namespace dovahlink::application {

// Outcome of processing one inbound message on an authenticated connection:
// zero or more response envelopes to send (most message types produce
// exactly one; subscribe can produce subscription_ack plus a snapshot; a
// hard closure -- the session message cap, or an oversized frame -- produces
// none, see ProcessInboundMessage's doc), and whether the connection must
// be closed after sending them.
struct DispatchResult {
    std::vector<protocol::Envelope> responses;
    bool closeConnection = false;
};

// Processes one raw inbound WebSocket text message on an already-
// authenticated connection (post-hello_ack; hello itself is handled
// separately by HandleHello -- see handshake_handler.hpp -- this function
// is never called for it).
//
// Applies, in order: bounded JSON parsing and envelope decoding
// (protocol/bounded_json.hpp, protocol/envelope.hpp), a messageType
// allowlist (ping, capabilities, subscribe, snapshot_request -- anything
// else, including hello or a bridge-to-client-only type like state_snapshot,
// is rejected as malformed_message), session-ID validation (stale_session),
// replay/session-cap checking (replayed_message, or a hard close at the
// session message cap with no response -- ai/context/protocol/security.md:
// "the bridge closes the session before this bound is exceeded"), and
// per-connection inbound rate limiting (rate_limited). Anything that
// survives all of that is routed to the matching already-built handler.
//
// An oversized frame (protocol::BoundedJsonError::kFrameTooLarge) closes
// immediately with no response and is not counted as a violation, matching
// security.md's "do not attempt to send an error over an invalid or
// oversized frame" -- the same rule transport/websocket_session.hpp already
// applies at the framing layer. Every other rejection is recorded as one
// protocol violation (security::ViolationTracker); the connection closes
// once violations reach security.md's 3-in-30s limit.
//
// A successfully routed message resets the idle timeout
// (ConnectionTimeoutTracker::RecordActivity); a rejected one does not, so a
// client that never sends anything valid cannot hold the connection open
// past the idle timeout purely by generating rejections.
[[nodiscard]] DispatchResult ProcessInboundMessage(
    const std::string& rawMessage, const std::string& sessionId, ConnectionId connection,
    SessionManager& sessionManager, ReplayGuard& replayGuard, security::ViolationTracker& violations,
    security::InboundMessageRateLimiter& rateLimiter, ConnectionTimeoutTracker& timeoutTracker,
    const CharacterStateProvider& stateProvider, RevisionTracker& revisions,
    std::chrono::steady_clock::time_point steadyNow, std::chrono::system_clock::time_point wallNow);

}  // namespace dovahlink::application
