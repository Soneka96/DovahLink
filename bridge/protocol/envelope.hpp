#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

// The canonical v1 message envelope. See protocol/schema/README.md.
struct Envelope {
    std::int64_t protocolVersion = 0;
    std::string messageType;
    std::string messageId;
    // nullopt represents a JSON null. Null only for hello, and for an error
    // that rejects a connection before a session exists on that socket.
    std::optional<std::string> sessionId;
    std::optional<std::string> correlationId;
    boost::json::object payload;
};

using EnvelopeError = DecodeError;

// Decodes one already-parsed JSON value (see bounded_json.hpp) into its envelope.
// Unknown top-level fields are tolerated per ai/context/protocol/compatibility.md;
// a missing or mistyped required field is rejected. Every rejection here maps to
// the wire error code `malformed_message`.
std::expected<Envelope, EnvelopeError> DecodeEnvelope(const boost::json::value& message);

}  // namespace dovahlink::protocol
