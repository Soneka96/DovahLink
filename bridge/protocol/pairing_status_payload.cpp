#include "protocol/pairing_status_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <algorithm>
#include <array>
#include <utility>

namespace dovahlink::protocol {

namespace {

constexpr std::array<std::string_view, 4> kValidPairingStates = {
    "unavailable",
    "available",
    "in_progress",
    "other_device_pairing",
};

} //  namespace

std::expected<PairingStatusPayload, MessageError>
DecodePairingStatusPayload(const boost::json::object& payload) {
    auto state = DecodeNonEmptyString(RequireField(payload, "state"), "state");
    if (!state) {
        return std::unexpected(state.error());
    }
    if (std::ranges::find(kValidPairingStates, *state) ==
        kValidPairingStates.end()) {
        return Fail("state must be one of the registered pairing states");
    }
    auto expiresInSeconds = DecodeOptionalNonNegativeInt(
        RequireField(payload, "expiresInSeconds"), "expiresInSeconds");
    if (!expiresInSeconds) {
        return std::unexpected(expiresInSeconds.error());
    }
    return PairingStatusPayload{.state = std::move(*state),
                                .expiresInSeconds = *expiresInSeconds};
}

boost::json::object
EncodePairingStatusPayload(const PairingStatusPayload& payload) {
    boost::json::object obj;
    obj["state"] = payload.state;
    //  protocol/schema/README.md: expiresInSeconds is always present as null for
    //  every other state, but genuinely omitted -- not merely null -- for
    //  other_device_pairing, so a competing device's request reveals nothing else
    //  about the owning device or its code.
    if (payload.state != "other_device_pairing") {
        obj["expiresInSeconds"] =
            payload.expiresInSeconds.has_value()
                ? boost::json::value(*payload.expiresInSeconds)
                : boost::json::value(nullptr);
    }
    return obj;
}

} //  namespace dovahlink::protocol
