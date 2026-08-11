#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"

#include <chrono>

namespace dovahlink::application {

// The only registered protocol version this bridge supports.
// protocol/schema/README.md: "protocolVersion: 0 is reserved for
// pre-negotiation hello and hello_ack"; v1 is the only other registered
// version (protocol/messages.hpp's message_type/state_area registrations).
inline constexpr std::int64_t kSupportedProtocolVersion = 1;

// The outcome of processing one hello attempt: exactly one response envelope
// to send, and whether the connection must be closed after sending it. This
// step never leaves an unauthenticated connection open for a retry -- every
// failure path closes, matching security::FailedTokenThrottle's own "one
// instance shared across every connection attempt" design: retries happen
// as fresh connections, throttled globally, not as repeated hello messages
// on one still-open socket (see handshake_handler.cpp for the full
// reasoning).
struct HandshakeResult {
    protocol::Envelope response;
    bool closeConnection = false;
};

// Processes exactly one already-decoded envelope that the caller has
// confirmed has messageType "hello" (routing/dispatch on messageType is the
// caller's job, not this function's -- see application/coordinator.hpp for
// where the rest of the connection lifecycle is owned). Validates the
// requested protocol version, consumes the one-time token, creates the
// session, and returns either a hello_ack or a mapped error.
//
// Does not read or write a socket: the caller sends `response` and closes
// the connection when `closeConnection` is true. tokenStore, sessionManager,
// and timeoutTracker are per-connection; tokenThrottle is shared globally
// across every connection attempt.
[[nodiscard]] HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope,
                                           security::TokenStore& tokenStore,
                                           security::FailedTokenThrottle& tokenThrottle,
                                           SessionManager& sessionManager, ConnectionId connection,
                                           ConnectionTimeoutTracker& timeoutTracker,
                                           std::chrono::steady_clock::time_point now);

}  // namespace dovahlink::application
