#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Represents the canonical protocol message envelope.
struct Envelope {
  /// Registered message type identifier.
  std::string messageType;
  /// Message identifier used for correlation and replay protection.
  std::string messageId;
  /// Session identifier, absent only for pre-session messages such as hello.
  std::optional<std::string> sessionId;
  /// Identifier of the message being answered, when the message is correlated.
  std::optional<std::string> correlationId;
  /// Message-specific JSON object payload.
  boost::json::object payload;
  /// Identity of the bridge instance that produced this message. `null` on
  /// the client's own `hello` (the client does not know it yet) and on a
  /// narrow set of early connection-hygiene rejections the bridge cannot
  /// attach an identity to; present otherwise.
  std::optional<std::string> bridgeInstanceId;
  /// Identity of the currently loaded play context, when one is active.
  /// `null` outside an active play context (main menu, before any load, or
  /// after a return to the main menu) -- genuine semantic absence, not a
  /// placeholder.
  std::optional<std::string> playContextId;
  /// Identity of the logical client, established at `hello`. `null` before
  /// `hello` completes, the same shape `sessionId` already uses.
  std::optional<std::string> clientId;
};

/// Reports an envelope decoding failure.
using EnvelopeError = DecodeError;

/// Decodes an already-bounded JSON value into an envelope.
///
/// Unknown top-level fields are tolerated; missing or mistyped required fields
/// are rejected as malformed messages and map to the `malformed_message` wire
/// error code.
std::expected<Envelope, EnvelopeError>
DecodeEnvelope(const boost::json::value &message);

/// Serializes an envelope without validating its message-specific payload.
std::string EncodeEnvelope(const Envelope &envelope);

/// Builds an outbound envelope with a cryptographically generated message ID.
/// Returns `std::nullopt` when secure message-ID generation fails; callers must
/// not send a missing result and should use their own error handling.
std::optional<Envelope> BuildEnvelope(std::string messageType,
                                      std::optional<std::string> sessionId,
                                      std::optional<std::string> correlationId,
                                      boost::json::object payload);

} // namespace dovahlink::protocol
