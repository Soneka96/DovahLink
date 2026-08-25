#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Decoded client authentication and identity data. `auth.method` is one of
/// `"one_time_local_token"` (developer authentication, `security.md`'s
/// "Developer authentication"), `"unpaired"` (no credential yet -- admits a
/// trust-restricted session solely to run the pairing flow), or
/// `"trusted_device_credential"` (a persisted pairing credential, for an
/// ordinary reconnect). See `security.md`'s "Hello authentication and session
/// trust tiers".
struct HelloPayload {
  /// Endpoint role declared by the client.
  std::string endpoint;
  /// Authentication method identifier.
  std::string authMethod;
  /// Authentication token supplied by the client. Absent only for `auth.method:
  /// "unpaired"`, which has no credential to present yet.
  std::optional<std::string> authToken;
  /// Identifier of the logical client establishing this connection. Shape-only
  /// here: a required non-empty string is enforced by the payload decoder; the
  /// application layer validates it against an existing session.
  std::string clientId;
};

/// Decodes a client hello payload and validates its supported authentication
/// form.
std::expected<HelloPayload, MessageError>
DecodeHelloPayload(const boost::json::object &payload);

} // namespace dovahlink::protocol
