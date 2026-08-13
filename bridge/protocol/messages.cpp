#include "protocol/messages.hpp"

#include "protocol/json_field_decoders.hpp"

#include <boost/json/array.hpp>
#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <utility>

namespace dovahlink::protocol {

namespace {

/**
 * @brief Creates an unexpected message error with the specified reason.
 *
 * @param reason Description of the message error.
 * @return An unexpected result containing the message error.
 */
std::unexpected<MessageError> Fail(std::string reason) {
    return std::unexpected(MessageError{std::move(reason)});
}

/**
 * @brief Retrieves a field from a JSON object.
 *
 * @param obj JSON object to search.
 * @param key Field name to locate.
 * @return Pointer to the field value, or `nullptr` if the field is absent.
 */
const boost::json::value* RequireField(const boost::json::object& obj, std::string_view key) {
    return obj.if_contains(key);
}

/**
 * @brief Decodes a required JSON array containing non-empty strings.
 *
 * @param value JSON value to decode.
 * @param fieldName Field name used in validation errors.
 * @return std::vector<std::string> Decoded strings.
 */
std::expected<std::vector<std::string>, MessageError> DecodeStringArray(const boost::json::value* value,
                                                                         std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (!value->is_array()) {
        return Fail(std::string(fieldName) + " must be an array");
    }
    std::vector<std::string> result;
    result.reserve(value->get_array().size());
    for (const boost::json::value& item : value->get_array()) {
        if (!item.is_string() || item.get_string().empty()) {
            return Fail(std::string(fieldName) + " items must be non-empty strings");
        }
        result.emplace_back(item.get_string());
    }
    return result;
}

}  /**
 * @brief Decodes and validates a client hello payload.
 *
 * @param payload JSON object containing the hello message fields.
 * @return HelloPayload on success, or a MessageError if the endpoint, supported
 *         protocol versions, authentication method, or token is invalid.
 */

std::expected<HelloPayload, MessageError> DecodeHelloPayload(const boost::json::object& payload) {
    auto endpoint = DecodeNonEmptyString(RequireField(payload, "endpoint"), "endpoint");
    if (!endpoint) {
        return std::unexpected(endpoint.error());
    }
    if (*endpoint != "client") {
        return Fail("endpoint must be 'client' in v1");
    }

    auto supportedProtocolVersions = [&]() -> std::expected<std::vector<std::int64_t>, MessageError> {
        const boost::json::value* value = RequireField(payload, "supportedProtocolVersions");
        if (!value) {
            return Fail("missing required field: supportedProtocolVersions");
        }
        if (!value->is_array()) {
            return Fail("supportedProtocolVersions must be an array");
        }
        std::vector<std::int64_t> versions;
        versions.reserve(value->get_array().size());
        for (const boost::json::value& item : value->get_array()) {
            auto version = DecodeNonNegativeInt(&item, "supportedProtocolVersions item");
            if (!version) {
                return std::unexpected(version.error());
            }
            versions.push_back(*version);
        }
        return versions;
    }();
    if (!supportedProtocolVersions) {
        return std::unexpected(supportedProtocolVersions.error());
    }

    const boost::json::value* authValue = RequireField(payload, "auth");
    if (!authValue || !authValue->is_object()) {
        return Fail("auth must be an object");
    }
    const boost::json::object& authObj = authValue->get_object();

    auto authMethod = DecodeNonEmptyString(RequireField(authObj, "method"), "auth.method");
    if (!authMethod) {
        return std::unexpected(authMethod.error());
    }
    if (*authMethod != "one_time_local_token") {
        return Fail("auth.method must be 'one_time_local_token' in v1");
    }

    auto authToken = DecodeNonEmptyString(RequireField(authObj, "token"), "auth.token");
    if (!authToken) {
        return std::unexpected(authToken.error());
    }

    return HelloPayload{
        .endpoint = std::move(*endpoint),
        .supportedProtocolVersions = std::move(*supportedProtocolVersions),
        .authMethod = std::move(*authMethod),
        .authToken = std::move(*authToken),
    };
}

/**
 * @brief Decodes a hello acknowledgment payload.
 *
 * @param payload JSON object containing the selected protocol version.
 * @return The decoded hello acknowledgment, or a validation error.
 */
std::expected<HelloAckPayload, MessageError> DecodeHelloAckPayload(const boost::json::object& payload) {
    auto selectedProtocolVersion =
        DecodeNonNegativeInt(RequireField(payload, "selectedProtocolVersion"), "selectedProtocolVersion");
    if (!selectedProtocolVersion) {
        return std::unexpected(selectedProtocolVersion.error());
    }
    return HelloAckPayload{.selectedProtocolVersion = *selectedProtocolVersion};
}

/**
 * @brief Serializes a hello acknowledgment payload.
 *
 * @return JSON object containing the selected protocol version.
 */
boost::json::object EncodeHelloAckPayload(const HelloAckPayload& payload) {
    boost::json::object obj;
    obj["selectedProtocolVersion"] = payload.selectedProtocolVersion;
    return obj;
}

/**
 * @brief Decodes a capabilities payload.
 *
 * @param payload JSON object containing the capabilities array.
 * @return Decoded capabilities, or a validation error.
 */
std::expected<CapabilitiesPayload, MessageError> DecodeCapabilitiesPayload(const boost::json::object& payload) {
    const boost::json::value* capabilitiesValue = RequireField(payload, "capabilities");
    if (!capabilitiesValue) {
        return Fail("missing required field: capabilities");
    }
    if (!capabilitiesValue->is_array()) {
        return Fail("capabilities must be an array");
    }

    std::vector<Capability> capabilities;
    capabilities.reserve(capabilitiesValue->get_array().size());
    for (const boost::json::value& item : capabilitiesValue->get_array()) {
        if (!item.is_object()) {
            return Fail("each capability must be an object");
        }
        const boost::json::object& capObj = item.get_object();

        auto id = DecodeNonEmptyString(RequireField(capObj, "id"), "capabilities[].id");
        if (!id) {
            return std::unexpected(id.error());
        }
        auto version = DecodeNonNegativeInt(RequireField(capObj, "version"), "capabilities[].version");
        if (!version) {
            return std::unexpected(version.error());
        }
        capabilities.push_back(Capability{.id = std::move(*id), .version = *version});
    }

    return CapabilitiesPayload{.capabilities = std::move(capabilities)};
}

/**
 * @brief Serializes capabilities into a JSON payload.
 *
 * @param payload Capabilities to serialize.
 * @return JSON object containing the capability identifiers and versions.
 */
boost::json::object EncodeCapabilitiesPayload(const CapabilitiesPayload& payload) {
    boost::json::array capabilities;
    capabilities.reserve(payload.capabilities.size());
    for (const Capability& capability : payload.capabilities) {
        boost::json::object capObj;
        capObj["id"] = capability.id;
        capObj["version"] = capability.version;
        capabilities.push_back(std::move(capObj));
    }
    boost::json::object obj;
    obj["capabilities"] = std::move(capabilities);
    return obj;
}

/**
 * @brief Decodes a subscription request payload.
 *
 * @param payload JSON object containing the state-area names to subscribe to.
 * @return SubscribePayload on success; a MessageError if the state-area list is missing, empty, or invalid.
 */
std::expected<SubscribePayload, MessageError> DecodeSubscribePayload(const boost::json::object& payload) {
    auto stateAreas = DecodeStringArray(RequireField(payload, "stateAreas"), "stateAreas");
    if (!stateAreas) {
        return std::unexpected(stateAreas.error());
    }
    return SubscribePayload{.stateAreas = std::move(*stateAreas)};
}

/**
 * @brief Decodes a subscription acknowledgment payload.
 *
 * @param payload JSON object containing accepted and rejected state-area arrays.
 * @return Decoded subscription acknowledgment, or a message error when either array is invalid.
 */
std::expected<SubscriptionAckPayload, MessageError> DecodeSubscriptionAckPayload(
    const boost::json::object& payload) {
    auto accepted = DecodeStringArray(RequireField(payload, "acceptedStateAreas"), "acceptedStateAreas");
    if (!accepted) {
        return std::unexpected(accepted.error());
    }
    auto rejected = DecodeStringArray(RequireField(payload, "rejectedStateAreas"), "rejectedStateAreas");
    if (!rejected) {
        return std::unexpected(rejected.error());
    }
    return SubscriptionAckPayload{
        .acceptedStateAreas = std::move(*accepted),
        .rejectedStateAreas = std::move(*rejected),
    };
}

/**
 * @brief Encodes subscription acknowledgment state areas as a JSON object.
 *
 * @param payload Subscription acknowledgment containing accepted and rejected state-area names.
 * @return JSON object containing the accepted and rejected state-area arrays.
 */
boost::json::object EncodeSubscriptionAckPayload(const SubscriptionAckPayload& payload) {
    boost::json::array accepted;
    accepted.reserve(payload.acceptedStateAreas.size());
    for (const std::string& area : payload.acceptedStateAreas) {
        accepted.push_back(boost::json::value(area));
    }
    boost::json::array rejected;
    rejected.reserve(payload.rejectedStateAreas.size());
    for (const std::string& area : payload.rejectedStateAreas) {
        rejected.push_back(boost::json::value(area));
    }
    boost::json::object obj;
    obj["acceptedStateAreas"] = std::move(accepted);
    obj["rejectedStateAreas"] = std::move(rejected);
    return obj;
}

/**
 * @brief Decodes a snapshot request payload.
 *
 * @param payload JSON object containing the state area and optional known revision.
 * @return Decoded snapshot request, or a validation error.
 */
std::expected<SnapshotRequestPayload, MessageError> DecodeSnapshotRequestPayload(
    const boost::json::object& payload) {
    auto stateArea = DecodeNonEmptyString(RequireField(payload, "stateArea"), "stateArea");
    if (!stateArea) {
        return std::unexpected(stateArea.error());
    }

    std::optional<std::int64_t> knownRevision;
    if (const boost::json::value* knownRevisionValue = RequireField(payload, "knownRevision")) {
        auto decoded = DecodeNonNegativeInt(knownRevisionValue, "knownRevision");
        if (!decoded) {
            return std::unexpected(decoded.error());
        }
        knownRevision = *decoded;
    }

    return SnapshotRequestPayload{.stateArea = std::move(*stateArea), .knownRevision = knownRevision};
}

/**
 * @brief Decodes a state snapshot payload.
 *
 * @param payload JSON object containing the state area, revision, occurrence time, and snapshot data.
 * @return The decoded snapshot, or a MessageError if a required field is invalid or missing.
 */
std::expected<StateSnapshotPayload, MessageError> DecodeStateSnapshotPayload(
    const boost::json::object& payload) {
    auto stateArea = DecodeNonEmptyString(RequireField(payload, "stateArea"), "stateArea");
    if (!stateArea) {
        return std::unexpected(stateArea.error());
    }
    auto revision = DecodeNonNegativeInt(RequireField(payload, "revision"), "revision");
    if (!revision) {
        return std::unexpected(revision.error());
    }
    auto occurredAt = DecodeNonEmptyString(RequireField(payload, "occurredAt"), "occurredAt");
    if (!occurredAt) {
        return std::unexpected(occurredAt.error());
    }
    const boost::json::value* dataValue = RequireField(payload, "data");
    if (!dataValue || !dataValue->is_object()) {
        return Fail("data must be an object");
    }

    return StateSnapshotPayload{
        .stateArea = std::move(*stateArea),
        .revision = *revision,
        .occurredAt = std::move(*occurredAt),
        .data = dataValue->get_object(),
    };
}

/**
 * @brief Serializes a state snapshot payload to a JSON object.
 *
 * @param payload State snapshot payload to serialize.
 * @return JSON object containing the state area, revision, timestamp, and data.
 */
boost::json::object EncodeStateSnapshotPayload(const StateSnapshotPayload& payload) {
    boost::json::object obj;
    obj["stateArea"] = payload.stateArea;
    obj["revision"] = payload.revision;
    obj["occurredAt"] = payload.occurredAt;
    obj["data"] = payload.data;
    return obj;
}

/**
 * @brief Decodes a state event payload.
 *
 * @param payload JSON object containing the state area, revisions, occurrence timestamp, and event data.
 * @return Decoded state event payload, or a message error when a required field is invalid.
 */
std::expected<StateEventPayload, MessageError> DecodeStateEventPayload(const boost::json::object& payload) {
    auto stateArea = DecodeNonEmptyString(RequireField(payload, "stateArea"), "stateArea");
    if (!stateArea) {
        return std::unexpected(stateArea.error());
    }
    auto baseRevision = DecodeNonNegativeInt(RequireField(payload, "baseRevision"), "baseRevision");
    if (!baseRevision) {
        return std::unexpected(baseRevision.error());
    }
    auto revision = DecodeNonNegativeInt(RequireField(payload, "revision"), "revision");
    if (!revision) {
        return std::unexpected(revision.error());
    }
    auto occurredAt = DecodeNonEmptyString(RequireField(payload, "occurredAt"), "occurredAt");
    if (!occurredAt) {
        return std::unexpected(occurredAt.error());
    }
    const boost::json::value* dataValue = RequireField(payload, "data");
    if (!dataValue || !dataValue->is_object()) {
        return Fail("data must be an object");
    }

    return StateEventPayload{
        .stateArea = std::move(*stateArea),
        .baseRevision = *baseRevision,
        .revision = *revision,
        .occurredAt = std::move(*occurredAt),
        .data = dataValue->get_object(),
    };
}

namespace {

/**
 * @brief Decodes a required resource field as either null or a numeric resource value.
 *
 * @param value JSON value for the resource field.
 * @param fieldName Field name used in validation errors.
 * @return An empty optional for JSON null, or a resource containing numeric current and maximum values.
 */
std::expected<std::optional<ResourceValue>, MessageError> DecodeResourceValue(const boost::json::value* value,
                                                                               std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (value->is_null()) {
        return std::optional<ResourceValue>{};
    }
    if (!value->is_object()) {
        return Fail(std::string(fieldName) + " must be an object or null");
    }
    const boost::json::object& obj = value->get_object();

    const boost::json::value* currentValue = RequireField(obj, "current");
    const boost::json::value* maximumValue = RequireField(obj, "maximum");
    if (!currentValue || !currentValue->is_number() || !maximumValue || !maximumValue->is_number()) {
        return Fail(std::string(fieldName) + ".current and .maximum must be numbers");
    }

    return std::optional<ResourceValue>{ResourceValue{
        .current = currentValue->to_number<double>(),
        .maximum = maximumValue->to_number<double>(),
    }};
}

}  /**
 * @brief Decodes a character state from a JSON object.
 *
 * Validates the level and health, magicka, and stamina resource fields. The
 * level and resource values may be null.
 *
 * @param data JSON object containing the character state.
 * @return Decoded character state, or a validation error.
 */

std::expected<CharacterState, MessageError> DecodeCharacterState(const boost::json::object& data) {
    const boost::json::value* levelValue = RequireField(data, "level");
    if (!levelValue) {
        return Fail("missing required field: level");
    }
    std::optional<std::int64_t> level;
    if (!levelValue->is_null()) {
        auto decodedLevel = DecodeNonNegativeInt(levelValue, "level");
        if (!decodedLevel) {
            return std::unexpected(decodedLevel.error());
        }
        level = *decodedLevel;
    }

    auto health = DecodeResourceValue(RequireField(data, "health"), "health");
    if (!health) {
        return std::unexpected(health.error());
    }
    auto magicka = DecodeResourceValue(RequireField(data, "magicka"), "magicka");
    if (!magicka) {
        return std::unexpected(magicka.error());
    }
    auto stamina = DecodeResourceValue(RequireField(data, "stamina"), "stamina");
    if (!stamina) {
        return std::unexpected(stamina.error());
    }

    return CharacterState{
        .level = level,
        .health = *health,
        .magicka = *magicka,
        .stamina = *stamina,
    };
}

/**
 * @brief Decodes an error payload.
 *
 * @param payload JSON object containing the error code, message, retryability, and optional details.
 * @return ErrorPayload containing the decoded error information, or a MessageError if validation fails.
 */
std::expected<ErrorPayload, MessageError> DecodeErrorPayload(const boost::json::object& payload) {
    auto code = DecodeNonEmptyString(RequireField(payload, "code"), "code");
    if (!code) {
        return std::unexpected(code.error());
    }
    auto message = DecodeNonEmptyString(RequireField(payload, "message"), "message");
    if (!message) {
        return std::unexpected(message.error());
    }

    const boost::json::value* retryableValue = RequireField(payload, "retryable");
    if (!retryableValue || !retryableValue->is_bool()) {
        return Fail("retryable must be a boolean");
    }

    std::optional<boost::json::value> details;
    if (const boost::json::value* detailsValue = RequireField(payload, "details")) {
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

/**
 * @brief Serializes an error payload to a JSON object.
 *
 * @param payload Error payload to serialize.
 * @return JSON object containing the error code, message, retryable flag, and details.
 */
boost::json::object EncodeErrorPayload(const ErrorPayload& payload) {
    boost::json::object obj;
    obj["code"] = payload.code;
    obj["message"] = payload.message;
    obj["retryable"] = payload.retryable;
    obj["details"] = payload.details.has_value() ? *payload.details : boost::json::value(nullptr);
    return obj;
}

/**
 * @brief Constructs an error envelope with the specified protocol and error details.
 *
 * @param correlationId Optional identifier of the message associated with the error.
 * @param protocolVersion Protocol version for the envelope.
 * @param sessionId Optional session identifier.
 * @param code Error code.
 * @param message Human-readable error message.
 * @param retryable Whether the operation may be retried.
 * @return Envelope containing the error payload. Uses `"csprng-unavailable"` as the
 *         message ID if secure message-ID generation fails.
 */
Envelope BuildErrorEnvelope(std::optional<std::string> correlationId, std::int64_t protocolVersion,
                             std::optional<std::string> sessionId, std::string code, std::string message,
                             bool retryable) {
    boost::json::object payload = EncodeErrorPayload(ErrorPayload{
        .code = std::move(code),
        .message = std::move(message),
        .retryable = retryable,
        .details = std::nullopt,
    });
    auto envelope =
        BuildEnvelope(protocolVersion, std::string(message_type::kError), sessionId, correlationId, payload);
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    // GenerateOpaqueId failed inside BuildEnvelope -- unreachable in
    // practice (security/csprng.hpp). A fixed, non-random messageId here is
    // the one place this function cannot honor the "cryptographically
    // random messageId" requirement, since the same broken primitive would
    // fail identically on any retry.
    return Envelope{
        .protocolVersion = protocolVersion,
        .messageType = std::string(message_type::kError),
        .messageId = "csprng-unavailable",
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = std::move(payload),
    };
}

}  // namespace dovahlink::protocol
