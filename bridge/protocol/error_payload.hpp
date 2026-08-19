#pragma once

#include "protocol/decode_error.hpp"
#include "protocol/envelope.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Structured error information returned by the bridge.
struct ErrorPayload {
    /// Stable machine-readable error code.
    std::string code;
    /// Human-readable error description.
    std::string message;
    /// Whether retrying the failed operation is allowed.
    bool retryable = false;
    /// Optional structured error details; absent represents JSON null on decode.
    std::optional<boost::json::value> details;
};

/// Decodes an error payload.
std::expected<ErrorPayload, MessageError> DecodeErrorPayload(const boost::json::object& payload);

/// Encodes an error payload, always including its nullable details field.
boost::json::object EncodeErrorPayload(const ErrorPayload& payload);

/// Builds a complete error envelope and always returns a usable envelope. Uses the
/// `csprng-unavailable` sentinel message ID if secure ID generation fails. `correlationId`
/// identifies the message being answered or is null when there is no correlation; `sessionId` is
/// null before a session exists and is otherwise the active session ID.
Envelope BuildErrorEnvelope(std::optional<std::string> correlationId, std::optional<std::string> sessionId,
                            std::string code, std::string message, bool retryable);

}  // namespace dovahlink::protocol
