#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

///  Bridge reply to `pairing_confirm` or `pairing_ack`, distinguished by
///  `outcome`.
struct PairingOutcomePayload {
    ///  One of `"credential_issued"`, `"trusted"`, `"already_trusted"`,
    ///  `"expired"`, `"invalid"`,
    ///  `"pacing_limited"`, `"hard_limit_reached"`, `"pending_not_found"`,
    ///  `"renotified"`,
    ///  `"renotify_cooldown"`, `"cancelled"`, `"already_idle"`. `"pacing_limited"`
    ///  and
    ///  `"hard_limit_reached"` replace the single undifferentiated
    ///  `"rate_limited"` earlier phases used: pacing blocks an attempt without
    ///  counting against the wrong-attempt budget, while the hard limit is the
    ///  terminal count of wrong attempts that destroys the challenge.
    ///  `"already_idle"` is shared by a manual renotify or cancel request that
    ///  finds no active challenge or pending credential owned by the requester --
    ///  the same "nothing to act on" meaning either way.
    std::string outcome;
    ///  Hex-encoded credential; present only for `"credential_issued"`,
    ///  `"trusted"`, and
    ///  `"already_trusted"`.
    std::optional<std::string> credential;
    ///  Administration-only identifier; present only for `"trusted"` and
    ///  `"already_trusted"`.
    std::optional<std::string> shortId;
    ///  Echoed presentation-only label; present only alongside
    ///  `credential`/`shortId` when the client supplied one.
    std::optional<std::string> displayName;
    ///  Remaining wait in seconds before another attempt is accepted; present for
    ///  `"pacing_limited"` (next evaluated `pairing_confirm`) and
    ///  `"renotify_cooldown"` (next manual `pairing_renotify`).
    std::optional<std::int64_t> retryAfterSeconds;
};

///  Decodes a pairing outcome payload.
std::expected<PairingOutcomePayload, MessageError>
DecodePairingOutcomePayload(const boost::json::object& payload);

///  Encodes a pairing outcome payload.
boost::json::object
EncodePairingOutcomePayload(const PairingOutcomePayload& payload);

} //  namespace dovahlink::protocol
