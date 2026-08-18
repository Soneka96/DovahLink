#include "protocol/messages.hpp"

#include "protocol/json_field_decoders.hpp"

#include <boost/json/array.hpp>
#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <algorithm>
#include <array>
#include <utility>

namespace dovahlink::protocol {

namespace {

/// Creates an unexpected result containing a message-payload error.
std::unexpected<MessageError> Fail(std::string reason) {
    return std::unexpected(MessageError{std::move(reason)});
}

/// Returns a field value or `nullptr` when the field is absent.
const boost::json::value* RequireField(const boost::json::object& obj, std::string_view key) {
    return obj.if_contains(key);
}

/// Decodes an array containing only non-empty strings.
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

}
std::expected<HelloPayload, MessageError> DecodeHelloPayload(const boost::json::object& payload) {
    auto endpoint = DecodeNonEmptyString(RequireField(payload, "endpoint"), "endpoint");
    if (!endpoint) {
        return std::unexpected(endpoint.error());
    }
    if (*endpoint != "client") {
        return Fail("endpoint must be 'client'");
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
    if (*authMethod != "one_time_local_token" && *authMethod != "unpaired" &&
        *authMethod != "trusted_device_credential") {
        return Fail("auth.method must be one of: one_time_local_token, unpaired, trusted_device_credential");
    }

    // "unpaired" bootstraps a session with no credential to present yet; the other two methods
    // both require one (security.md's "Hello authentication and session trust tiers").
    std::optional<std::string> authToken;
    if (*authMethod == "unpaired") {
        if (RequireField(authObj, "token")) {
            return Fail("auth.token must be absent for auth.method 'unpaired'");
        }
    } else {
        auto decodedToken = DecodeNonEmptyString(RequireField(authObj, "token"), "auth.token");
        if (!decodedToken) {
            return std::unexpected(decodedToken.error());
        }
        authToken = std::move(*decodedToken);
    }

    auto clientId = DecodeNonEmptyString(RequireField(payload, "clientId"), "clientId");
    if (!clientId) {
        return std::unexpected(clientId.error());
    }

    return HelloPayload{
        .endpoint = std::move(*endpoint),
        .authMethod = std::move(*authMethod),
        .authToken = std::move(authToken),
        .clientId = std::move(*clientId),
    };
}

std::expected<HelloAckPayload, MessageError> DecodeHelloAckPayload(const boost::json::object& payload) {
    auto bridgeVersion = DecodeNonEmptyString(RequireField(payload, "bridgeVersion"), "bridgeVersion");
    if (!bridgeVersion) {
        return std::unexpected(bridgeVersion.error());
    }
    auto clientIdentityKind = DecodeNonEmptyString(RequireField(payload, "clientIdentityKind"), "clientIdentityKind");
    if (!clientIdentityKind) {
        return std::unexpected(clientIdentityKind.error());
    }
    return HelloAckPayload{
        .bridgeVersion = std::move(*bridgeVersion),
        .clientIdentityKind = std::move(*clientIdentityKind),
    };
}

boost::json::object EncodeHelloAckPayload(const HelloAckPayload& payload) {
    boost::json::object obj;
    obj["bridgeVersion"] = payload.bridgeVersion;
    obj["clientIdentityKind"] = payload.clientIdentityKind;
    return obj;
}

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

std::expected<SubscribePayload, MessageError> DecodeSubscribePayload(const boost::json::object& payload) {
    auto stateAreas = DecodeStringArray(RequireField(payload, "stateAreas"), "stateAreas");
    if (!stateAreas) {
        return std::unexpected(stateAreas.error());
    }
    return SubscribePayload{.stateAreas = std::move(*stateAreas)};
}

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

boost::json::object EncodeStateSnapshotPayload(const StateSnapshotPayload& payload) {
    boost::json::object obj;
    obj["stateArea"] = payload.stateArea;
    obj["revision"] = payload.revision;
    obj["occurredAt"] = payload.occurredAt;
    obj["data"] = payload.data;
    return obj;
}

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

/// Decodes a resource field as JSON null or a numeric current/maximum pair.
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

}
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

std::expected<PairingStatusPayload, MessageError> DecodePairingStatusPayload(const boost::json::object& payload) {
    auto state = DecodeNonEmptyString(RequireField(payload, "state"), "state");
    if (!state) {
        return std::unexpected(state.error());
    }
    if (*state != "unavailable" && *state != "available" && *state != "in_progress" &&
        *state != "other_device_pairing") {
        return Fail("state must be one of: unavailable, available, in_progress, other_device_pairing");
    }
    auto expiresInSeconds = DecodeOptionalNonNegativeInt(RequireField(payload, "expiresInSeconds"),
                                                          "expiresInSeconds");
    if (!expiresInSeconds) {
        return std::unexpected(expiresInSeconds.error());
    }
    return PairingStatusPayload{.state = std::move(*state), .expiresInSeconds = *expiresInSeconds};
}

boost::json::object EncodePairingStatusPayload(const PairingStatusPayload& payload) {
    boost::json::object obj;
    obj["state"] = payload.state;
    obj["expiresInSeconds"] = payload.expiresInSeconds.has_value() ? boost::json::value(*payload.expiresInSeconds)
                                                                     : boost::json::value(nullptr);
    return obj;
}

std::expected<PairingConfirmPayload, MessageError> DecodePairingConfirmPayload(const boost::json::object& payload) {
    auto code = DecodeNonEmptyString(RequireField(payload, "code"), "code");
    if (!code) {
        return std::unexpected(code.error());
    }
    auto displayName = DecodeOptionalString(RequireField(payload, "displayName"), "displayName");
    if (!displayName) {
        return std::unexpected(displayName.error());
    }
    return PairingConfirmPayload{.code = std::move(*code), .displayName = std::move(*displayName)};
}

std::expected<PairingAckPayload, MessageError> DecodePairingAckPayload(const boost::json::object& payload) {
    auto credential = DecodeNonEmptyString(RequireField(payload, "credential"), "credential");
    if (!credential) {
        return std::unexpected(credential.error());
    }
    return PairingAckPayload{.credential = std::move(*credential)};
}

namespace {

/// Registered `pairing_outcome.outcome` values.
constexpr std::array<std::string_view, 8> kValidPairingOutcomes = {
    "credential_issued", "trusted",       "already_trusted",    "expired",
    "invalid",           "pacing_limited", "hard_limit_reached", "pending_not_found",
};

}  // namespace

std::expected<PairingOutcomePayload, MessageError> DecodePairingOutcomePayload(const boost::json::object& payload) {
    auto outcome = DecodeNonEmptyString(RequireField(payload, "outcome"), "outcome");
    if (!outcome) {
        return std::unexpected(outcome.error());
    }
    if (std::ranges::find(kValidPairingOutcomes, *outcome) == kValidPairingOutcomes.end()) {
        return Fail("outcome must be one of the registered pairing outcomes");
    }

    auto credential = DecodeOptionalString(RequireField(payload, "credential"), "credential");
    if (!credential) {
        return std::unexpected(credential.error());
    }
    auto shortId = DecodeOptionalString(RequireField(payload, "shortId"), "shortId");
    if (!shortId) {
        return std::unexpected(shortId.error());
    }
    auto displayName = DecodeOptionalString(RequireField(payload, "displayName"), "displayName");
    if (!displayName) {
        return std::unexpected(displayName.error());
    }
    auto retryAfterSeconds = DecodeOptionalNonNegativeInt(RequireField(payload, "retryAfterSeconds"),
                                                           "retryAfterSeconds");
    if (!retryAfterSeconds) {
        return std::unexpected(retryAfterSeconds.error());
    }

    return PairingOutcomePayload{
        .outcome = std::move(*outcome),
        .credential = std::move(*credential),
        .shortId = std::move(*shortId),
        .displayName = std::move(*displayName),
        .retryAfterSeconds = *retryAfterSeconds,
    };
}

boost::json::object EncodePairingOutcomePayload(const PairingOutcomePayload& payload) {
    boost::json::object obj;
    obj["outcome"] = payload.outcome;
    obj["credential"] =
        payload.credential.has_value() ? boost::json::value(*payload.credential) : boost::json::value(nullptr);
    obj["shortId"] = payload.shortId.has_value() ? boost::json::value(*payload.shortId) : boost::json::value(nullptr);
    obj["displayName"] =
        payload.displayName.has_value() ? boost::json::value(*payload.displayName) : boost::json::value(nullptr);
    obj["retryAfterSeconds"] = payload.retryAfterSeconds.has_value() ? boost::json::value(*payload.retryAfterSeconds)
                                                                       : boost::json::value(nullptr);
    return obj;
}

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

boost::json::object EncodeErrorPayload(const ErrorPayload& payload) {
    boost::json::object obj;
    obj["code"] = payload.code;
    obj["message"] = payload.message;
    obj["retryable"] = payload.retryable;
    obj["details"] = payload.details.has_value() ? *payload.details : boost::json::value(nullptr);
    return obj;
}

Envelope BuildErrorEnvelope(std::optional<std::string> correlationId, std::optional<std::string> sessionId,
                             std::string code, std::string message, bool retryable) {
    boost::json::object payload = EncodeErrorPayload(ErrorPayload{
        .code = std::move(code),
        .message = std::move(message),
        .retryable = retryable,
        .details = std::nullopt,
    });
    auto envelope = BuildEnvelope(std::string(message_type::kError), sessionId, correlationId, payload);
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    // GenerateOpaqueId failed inside BuildEnvelope -- unreachable in
    // practice (security/csprng.hpp). A fixed, non-random messageId here is
    // the one place this function cannot honor the "cryptographically
    // random messageId" requirement, since the same broken primitive would
    // fail identically on any retry.
    return Envelope{
        .messageType = std::string(message_type::kError),
        .messageId = "csprng-unavailable",
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = std::move(payload),
    };
}

}  // namespace dovahlink::protocol
