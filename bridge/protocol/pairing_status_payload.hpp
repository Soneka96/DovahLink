#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Bridge report of pairing availability, sent in reply to `pairing_request`.
struct PairingStatusPayload {
    /// One of `"unavailable"`, `"available"`, `"in_progress"`, `"other_device_pairing"`.
    std::string state;
    /// The active challenge's remaining code validity in seconds; present for `"available"` (a
    /// freshly started challenge) and `"in_progress"` (the caller's own challenge, resumed).
    /// Never present for `"unavailable"` or `"other_device_pairing"` -- the latter reveals nothing
    /// about the owning device's code or its remaining time.
    std::optional<std::int64_t> expiresInSeconds;
};

/// Decodes a pairing status payload.
std::expected<PairingStatusPayload, MessageError> DecodePairingStatusPayload(const boost::json::object& payload);

/// Encodes a pairing status payload.
boost::json::object EncodePairingStatusPayload(const PairingStatusPayload& payload);

}  // namespace dovahlink::protocol
