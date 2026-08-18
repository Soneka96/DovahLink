#include "protocol/pairing_ack_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<PairingAckPayload, MessageError> DecodePairingAckPayload(const boost::json::object& payload) {
    auto credential = DecodeNonEmptyString(RequireField(payload, "credential"), "credential");
    if (!credential) {
        return std::unexpected(credential.error());
    }
    return PairingAckPayload{.credential = std::move(*credential)};
}

}  // namespace dovahlink::protocol
