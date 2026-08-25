#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <string>
#include <vector>

namespace dovahlink::protocol {

/// Bridge response listing accepted and rejected subscription areas.
struct SubscriptionAckPayload {
  /// State areas accepted by the bridge.
  std::vector<std::string> acceptedStateAreas;
  /// State areas rejected by the bridge.
  std::vector<std::string> rejectedStateAreas;
};

/// Decodes a subscription acknowledgment payload.
std::expected<SubscriptionAckPayload, MessageError>
DecodeSubscriptionAckPayload(const boost::json::object &payload);

/// Encodes a subscription acknowledgment payload.
boost::json::object
EncodeSubscriptionAckPayload(const SubscriptionAckPayload &payload);

} // namespace dovahlink::protocol
