#pragma once

#include "shared/enums.hpp"

#include <cstdint>
#include <string>

namespace dovahlink::application {

/// Opaque identifier for one transport-level connection.
using ConnectionId = std::uint64_t;

/// A complete, self-consistent snapshot of one active authenticated session,
/// returned by value -- never a reference into `SessionManager`'s own mutable
/// state -- so a caller inspects every field together without further locking
/// or any risk of reading a partially-updated record.
struct ActiveSession {
  /// Connection currently holding this session.
  ConnectionId connectionId;

  /// Server-issued session identifier.
  std::string sessionId;

  /// The client identity bound to this session, presented at `hello`.
  std::string clientId;

  /// The session's current message-type allowlist.
  SessionTrustTier trustTier;

  /// How the session authenticated at `hello`.
  SessionAuthMethod authMethod;
};

} // namespace dovahlink::application
