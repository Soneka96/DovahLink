#include "protocol/pairing_status_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

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

}  // namespace dovahlink::protocol
