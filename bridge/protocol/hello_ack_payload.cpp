#include "protocol/hello_ack_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

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

}  // namespace dovahlink::protocol
