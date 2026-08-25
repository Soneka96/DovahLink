#include "protocol/subscription_ack_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <boost/json/array.hpp>

#include <utility>

namespace dovahlink::protocol {

std::expected<SubscriptionAckPayload, MessageError>
DecodeSubscriptionAckPayload(const boost::json::object& payload) {
    auto accepted = DecodeStringArray(RequireField(payload, "acceptedStateAreas"),
                                      "acceptedStateAreas");
    if (!accepted) {
        return std::unexpected(accepted.error());
    }
    auto rejected = DecodeStringArray(RequireField(payload, "rejectedStateAreas"),
                                      "rejectedStateAreas");
    if (!rejected) {
        return std::unexpected(rejected.error());
    }
    return SubscriptionAckPayload{
        .acceptedStateAreas = std::move(*accepted),
        .rejectedStateAreas = std::move(*rejected),
    };
}

boost::json::object
EncodeSubscriptionAckPayload(const SubscriptionAckPayload& payload) {
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

} //  namespace dovahlink::protocol
