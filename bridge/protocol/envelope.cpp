#include "protocol/envelope.hpp"

#include "protocol/json_field_decoders.hpp"

#include <boost/json/object.hpp>
#include <boost/json/serialize.hpp>
#include <boost/json/value.hpp>

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

    auto protocolVersion = DecodeNonNegativeInt(protocolVersionValue, "protocolVersion");
    if (!protocolVersion) {
        return std::unexpected(protocolVersion.error());
    }

    auto messageType = DecodeNonEmptyString(messageTypeValue, "messageType");
    if (!messageType) {
        return std::unexpected(messageType.error());
    }

    auto messageId = DecodeNonEmptyString(messageIdValue, "messageId");
    if (!messageId) {
        return std::unexpected(messageId.error());
    }

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
    if (*messageType == "hello") {
        if (!sessionIdValue->is_null()) {
            return Fail("sessionId must be null for hello");
        }
    } else if (*messageType == "error") {
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
            return Fail("sessionId must be a non-empty string for '" + *messageType + "'");
        }
        sessionId = std::string(sessionIdValue->get_string());
    }

    if (!payloadValue->is_object()) {
        return Fail("payload must be an object");
    }

    return Envelope{
        .protocolVersion = *protocolVersion,
        .messageType = std::move(*messageType),
        .messageId = std::move(*messageId),
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = payloadValue->get_object(),
    };
}

std::string EncodeEnvelope(const Envelope& envelope) {
    boost::json::object obj;
    obj["protocolVersion"] = envelope.protocolVersion;
    obj["messageType"] = envelope.messageType;
    obj["messageId"] = envelope.messageId;
    obj["sessionId"] =
        envelope.sessionId.has_value() ? boost::json::value(*envelope.sessionId) : boost::json::value(nullptr);
    obj["correlationId"] = envelope.correlationId.has_value() ? boost::json::value(*envelope.correlationId)
                                                                : boost::json::value(nullptr);
    obj["payload"] = envelope.payload;
    return boost::json::serialize(obj);
}

}  // namespace dovahlink::protocol
