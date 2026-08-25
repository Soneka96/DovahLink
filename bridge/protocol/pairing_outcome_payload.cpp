#include "protocol/pairing_outcome_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <algorithm>
#include <array>
#include <string_view>
#include <utility>

namespace dovahlink::protocol {

namespace {

///  Registered `pairing_outcome.outcome` values.
constexpr std::array<std::string_view, 12> kValidPairingOutcomes = {
    "credential_issued",
    "trusted",
    "already_trusted",
    "expired",
    "invalid",
    "pacing_limited",
    "hard_limit_reached",
    "pending_not_found",
    "renotified",
    "renotify_cooldown",
    "cancelled",
    "already_idle",
};

} //  namespace

std::expected<PairingOutcomePayload, MessageError>
DecodePairingOutcomePayload(const boost::json::object& payload) {
    auto outcome =
        DecodeNonEmptyString(RequireField(payload, "outcome"), "outcome");
    if (!outcome) {
        return std::unexpected(outcome.error());
    }
    if (std::ranges::find(kValidPairingOutcomes, *outcome) ==
        kValidPairingOutcomes.end()) {
        return Fail("outcome must be one of the registered pairing outcomes");
    }

    auto credential =
        DecodeOptionalString(RequireField(payload, "credential"), "credential");
    if (!credential) {
        return std::unexpected(credential.error());
    }
    auto shortId =
        DecodeOptionalString(RequireField(payload, "shortId"), "shortId");
    if (!shortId) {
        return std::unexpected(shortId.error());
    }
    auto displayName =
        DecodeOptionalString(RequireField(payload, "displayName"), "displayName");
    if (!displayName) {
        return std::unexpected(displayName.error());
    }
    auto retryAfterSeconds = DecodeOptionalNonNegativeInt(
        RequireField(payload, "retryAfterSeconds"), "retryAfterSeconds");
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

boost::json::object
EncodePairingOutcomePayload(const PairingOutcomePayload& payload) {
    boost::json::object obj;
    obj["outcome"] = payload.outcome;
    obj["credential"] = payload.credential.has_value()
                            ? boost::json::value(*payload.credential)
                            : boost::json::value(nullptr);
    obj["shortId"] = payload.shortId.has_value()
                         ? boost::json::value(*payload.shortId)
                         : boost::json::value(nullptr);
    obj["displayName"] = payload.displayName.has_value()
                             ? boost::json::value(*payload.displayName)
                             : boost::json::value(nullptr);
    obj["retryAfterSeconds"] =
        payload.retryAfterSeconds.has_value()
            ? boost::json::value(*payload.retryAfterSeconds)
            : boost::json::value(nullptr);
    return obj;
}

} //  namespace dovahlink::protocol
