#include "protocol/error_payload.hpp"

#include "protocol/constants.hpp"
#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<ErrorPayload, MessageError>
DecodeErrorPayload(const boost::json::object& payload) {
    auto code = DecodeNonEmptyString(RequireField(payload, "code"), "code");
    if (!code) {
        return std::unexpected(code.error());
    }
    auto message =
        DecodeNonEmptyString(RequireField(payload, "message"), "message");
    if (!message) {
        return std::unexpected(message.error());
    }

    const boost::json::value* retryableValue = RequireField(payload, "retryable");
    if (!retryableValue || !retryableValue->is_bool()) {
        return Fail("retryable must be a boolean");
    }

    std::optional<boost::json::value> details;
    if (const boost::json::value* detailsValue =
            RequireField(payload, "details")) {
        if (!detailsValue->is_null()) {
            details = *detailsValue;
        }
    }

    return ErrorPayload{
        .code = std::move(*code),
        .message = std::move(*message),
        .retryable = retryableValue->get_bool(),
        .details = std::move(details),
    };
}

boost::json::object EncodeErrorPayload(const ErrorPayload& payload) {
    boost::json::object obj;
    obj["code"] = payload.code;
    obj["message"] = payload.message;
    obj["retryable"] = payload.retryable;
    obj["details"] = payload.details.has_value() ? *payload.details
                                                 : boost::json::value(nullptr);
    return obj;
}

Envelope BuildErrorEnvelope(std::optional<std::string> correlationId,
                            std::optional<std::string> sessionId,
                            std::string code, std::string message,
                            bool retryable) {
    boost::json::object payload = EncodeErrorPayload(ErrorPayload{
        .code = std::move(code),
        .message = std::move(message),
        .retryable = retryable,
        .details = std::nullopt,
    });
    auto envelope = BuildEnvelope(std::string(message_type::kError), sessionId,
                                  correlationId, payload);
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    //  GenerateOpaqueId failed inside BuildEnvelope -- unreachable in practice
    //  (security/csprng.hpp). A fixed, non-random messageId here is the one place
    //  this function cannot honor the "cryptographically random messageId"
    //  requirement, since the same broken primitive would fail identically on any
    //  retry.
    return Envelope{
        .messageType = std::string(message_type::kError),
        .messageId = "csprng-unavailable",
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = std::move(payload),
    };
}

} //  namespace dovahlink::protocol
