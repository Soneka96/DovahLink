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
    /// Protocol version selected for this message.
    std::int64_t protocolVersion = 0;
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
    /// Identity of the bridge instance that produced this message. Absent on
    /// `protocolVersion < 2`; present but possibly null once v2 identity
    /// (`protocolVersion >= 2`) applies, per `protocol/schema/README.md`'s v2
    /// section.
    std::optional<std::string> bridgeInstanceId;
    /// Identity of the currently loaded play context, when one is active.
    /// Absent on `protocolVersion < 2`; present-but-null under v2 outside an
    /// active play context (`NoContext`).
    std::optional<std::string> playContextId;
    /// Identity of the logical client, established at `hello`. Absent on
    /// `protocolVersion < 2` and before `hello` on a v2 connection, the same
    /// pre-session shape `sessionId` already uses.
    std::optional<std::string> clientId;
};

/// Reports an envelope decoding failure.
using EnvelopeError = DecodeError;

/// Decodes an already-bounded JSON value into an envelope.
///
/// Unknown top-level fields are tolerated; missing or mistyped required fields
/// are rejected as malformed messages and map to the `malformed_message` wire
/// error code.
std::expected<Envelope, EnvelopeError> DecodeEnvelope(const boost::json::value& message);

/// Serializes an envelope without validating its message-specific payload.
std::string EncodeEnvelope(const Envelope& envelope);

/// Builds an outbound envelope with a cryptographically generated message ID.
/// Returns `std::nullopt` when secure message-ID generation fails; callers must
/// not send a missing result and should use their own error handling.
std::optional<Envelope> BuildEnvelope(std::int64_t protocolVersion, std::string messageType,
                                       std::optional<std::string> sessionId,
                                       std::optional<std::string> correlationId,
                                       boost::json::object payload);

}  // namespace dovahlink::protocol
