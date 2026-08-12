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

// Encodes `envelope` to its canonical JSON wire text, using its `payload`
// field as-is. This function does not validate or build the payload's shape
// -- that is the caller's job, via the registered Encode*Payload functions
// in messages.hpp -- mirroring how DecodeEnvelope stays separate from the
// payload-specific Decode*Payload functions.
std::string EncodeEnvelope(const Envelope& envelope);

// Builds a complete envelope with a fresh cryptographically random
// messageId (security::GenerateOpaqueId), for any outbound message type.
// `payload` is already built by the caller via the registered
// Encode*Payload functions in messages.hpp. Returns nullopt only if
// GenerateOpaqueId fails -- an unreachable-in-practice CSPRNG failure, see
// security/csprng.hpp -- in which case the caller must not send a
// non-compliant message and should fall back to its own error handling
// (see messages.hpp's BuildErrorEnvelope for the pattern this follows).
std::optional<Envelope> BuildEnvelope(std::int64_t protocolVersion, std::string messageType,
                                       std::optional<std::string> sessionId,
                                       std::optional<std::string> correlationId,
                                       boost::json::object payload);

}  // namespace dovahlink::protocol
