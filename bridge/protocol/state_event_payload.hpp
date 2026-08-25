#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <string>

namespace dovahlink::protocol {

/// State event payload advancing a state area from one revision to another.
struct StateEventPayload {
  /// State area represented by the event.
  std::string stateArea;
  /// Revision immediately preceding this event.
  std::int64_t baseRevision = 0;
  /// Revision established by this event.
  std::int64_t revision = 0;
  /// Human-readable event timestamp; not an ordering source.
  std::string occurredAt;
  /// State-area-specific event data.
  boost::json::object data;
};

/// Decodes a state event payload's structural fields.
std::expected<StateEventPayload, MessageError>
DecodeStateEventPayload(const boost::json::object &payload);

} // namespace dovahlink::protocol
