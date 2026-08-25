#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <string>
#include <vector>

namespace dovahlink::protocol {

/// Client request for state-area subscriptions.
struct SubscribePayload {
  /// State areas requested by the client.
  std::vector<std::string> stateAreas;
};

/// Decodes a subscription request payload.
std::expected<SubscribePayload, MessageError>
DecodeSubscribePayload(const boost::json::object &payload);

} // namespace dovahlink::protocol
