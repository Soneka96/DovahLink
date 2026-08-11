#include "protocol/envelope.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <limits>
#include <utility>

namespace dovahlink::protocol {

namespace {

std::unexpected<EnvelopeError> Fail(std::string reason) {
    return std::unexpected(EnvelopeError{std::move(reason)});
}

}  // namespace

std::expected<Envelope, EnvelopeError> DecodeEnvelope(const boost::json::value& message) {
    if (!message.is_object()) {
        return Fail("envelope must be a JSON object");
    }
    const boost::json::object& obj = message.get_object();

    const boost::json::value* protocolVersionValue = obj.if_contains("protocolVersion");
    const boost::json::value* messageTypeValue = obj.if_contains("messageType");
    const boost::json::value* messageIdValue = obj.if_contains("messageId");
    const boost::json::value* sessionIdValue = obj.if_contains("sessionId");
    const boost::json::value* correlationIdValue = obj.if_contains("correlationId");
    const boost::json::value* payloadValue = obj.if_contains("payload");

    if (!protocolVersionValue || !messageTypeValue || !messageIdValue || !sessionIdValue ||
        !correlationIdValue || !payloadValue) {
        return Fail("missing required envelope field");
    }

    std::int64_t protocolVersion = 0;
    if (protocolVersionValue->is_int64()) {
        protocolVersion = protocolVersionValue->get_int64();
    } else if (protocolVersionValue->is_uint64()) {
        std::uint64_t asUint = protocolVersionValue->get_uint64();
        if (asUint > static_cast<std::uint64_t>(std::numeric_limits<std::int64_t>::max())) {
            return Fail("protocolVersion out of range");
        }
        protocolVersion = static_cast<std::int64_t>(asUint);
    } else {
        return Fail("protocolVersion must be an integer");
    }
    if (protocolVersion < 0) {
        return Fail("protocolVersion must be non-negative");
    }

    if (!messageTypeValue->is_string() || messageTypeValue->get_string().empty()) {
        return Fail("messageType must be a non-empty string");
    }
    std::string messageType(messageTypeValue->get_string());

    if (!messageIdValue->is_string() || messageIdValue->get_string().empty()) {
        return Fail("messageId must be a non-empty string");
    }
    std::string messageId(messageIdValue->get_string());

    std::optional<std::string> correlationId;
    if (correlationIdValue->is_string()) {
        if (correlationIdValue->get_string().empty()) {
            return Fail("correlationId must be null or a non-empty string");
        }
        correlationId = std::string(correlationIdValue->get_string());
    } else if (!correlationIdValue->is_null()) {
        return Fail("correlationId must be a string or null");
    }

    // sessionId is null only for hello, and may be null for an error that rejects a
    // connection before a session exists (protocol/schema/README.md).
    std::optional<std::string> sessionId;
    if (messageType == "hello") {
        if (!sessionIdValue->is_null()) {
            return Fail("sessionId must be null for hello");
        }
    } else if (messageType == "error") {
        if (sessionIdValue->is_string()) {
            if (sessionIdValue->get_string().empty()) {
                return Fail("sessionId must be null or a non-empty string for error");
            }
            sessionId = std::string(sessionIdValue->get_string());
        } else if (!sessionIdValue->is_null()) {
            return Fail("sessionId must be null or a non-empty string for error");
        }
    } else {
        if (!sessionIdValue->is_string() || sessionIdValue->get_string().empty()) {
            return Fail("sessionId must be a non-empty string for '" + messageType + "'");
        }
        sessionId = std::string(sessionIdValue->get_string());
    }

    if (!payloadValue->is_object()) {
        return Fail("payload must be an object");
    }

    return Envelope{
        .protocolVersion = protocolVersion,
        .messageType = std::move(messageType),
        .messageId = std::move(messageId),
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = payloadValue->get_object(),
    };
}

}  // namespace dovahlink::protocol
