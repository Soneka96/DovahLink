#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <string>

namespace dovahlink::protocol {

/// State snapshot payload establishing a revision baseline.
struct StateSnapshotPayload {
  /// State area represented by the snapshot.
  std::string stateArea;
  /// Revision established by the snapshot.
  std::int64_t revision = 0;
  /// Human-readable event timestamp; not an ordering source.
  std::string occurredAt;
  /// State-area-specific snapshot data.
  boost::json::object data;
};

/// Decodes a state snapshot payload.
std::expected<StateSnapshotPayload, MessageError>
DecodeStateSnapshotPayload(const boost::json::object &payload);

/// Encodes a state snapshot payload.
boost::json::object
EncodeStateSnapshotPayload(const StateSnapshotPayload &payload);

} // namespace dovahlink::protocol
