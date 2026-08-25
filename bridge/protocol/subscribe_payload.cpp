#include "protocol/subscribe_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<SubscribePayload, MessageError>
DecodeSubscribePayload(const boost::json::object &payload) {
  auto stateAreas =
      DecodeStringArray(RequireField(payload, "stateAreas"), "stateAreas");
  if (!stateAreas) {
    return std::unexpected(stateAreas.error());
  }
  return SubscribePayload{.stateAreas = std::move(*stateAreas)};
}

} // namespace dovahlink::protocol
