#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Bridge reply to `pairing_confirm` or `pairing_ack`, distinguished by `outcome`.
struct PairingOutcomePayload {
    /// One of `"credential_issued"`, `"trusted"`, `"already_trusted"`, `"expired"`, `"invalid"`,
    /// `"pacing_limited"`, `"hard_limit_reached"`, `"pending_not_found"`. `"pacing_limited"` and
    /// `"hard_limit_reached"` replace the single undifferentiated `"rate_limited"` earlier phases
    /// used: pacing blocks an attempt without counting against the wrong-attempt budget, while
    /// the hard limit is the terminal count of wrong attempts that destroys the challenge.
    std::string outcome;
    /// Hex-encoded credential; present only for `"credential_issued"`, `"trusted"`, and
    /// `"already_trusted"`.
    std::optional<std::string> credential;
    /// Administration-only identifier; present only for `"trusted"` and `"already_trusted"`.
    std::optional<std::string> shortId;
    /// Echoed presentation-only label; present only alongside `credential`/`shortId` when the
    /// client supplied one.
    std::optional<std::string> displayName;
    /// Remaining wait in seconds before another evaluated `pairing_confirm` attempt is accepted;
    /// present only for `"pacing_limited"`.
    std::optional<std::int64_t> retryAfterSeconds;
};

/// Decodes a pairing outcome payload.
std::expected<PairingOutcomePayload, MessageError> DecodePairingOutcomePayload(const boost::json::object& payload);

/// Encodes a pairing outcome payload.
boost::json::object EncodePairingOutcomePayload(const PairingOutcomePayload& payload);

}  // namespace dovahlink::protocol
