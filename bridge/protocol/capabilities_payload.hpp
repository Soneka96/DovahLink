#pragma once

#include "protocol/capability.hpp"
#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <vector>

namespace dovahlink::protocol {

///  Collection of capabilities advertised by an endpoint.
struct CapabilitiesPayload {
    ///  Capabilities included in the message.
    std::vector<Capability> capabilities;
};

///  Decodes a capabilities payload.
std::expected<CapabilitiesPayload, MessageError>
DecodeCapabilitiesPayload(const boost::json::object& payload);

///  Encodes a capabilities payload.
boost::json::object
EncodeCapabilitiesPayload(const CapabilitiesPayload& payload);

} //  namespace dovahlink::protocol
