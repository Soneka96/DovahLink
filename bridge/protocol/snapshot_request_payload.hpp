#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Client request for a state snapshot and optional known revision.
struct SnapshotRequestPayload {
    /// State area whose snapshot is requested.
    std::string stateArea;
    /// Client's latest known revision, when available.
    std::optional<std::int64_t> knownRevision;
};

/// Decodes a snapshot request payload.
std::expected<SnapshotRequestPayload, MessageError> DecodeSnapshotRequestPayload(
    const boost::json::object& payload);

}  // namespace dovahlink::protocol
