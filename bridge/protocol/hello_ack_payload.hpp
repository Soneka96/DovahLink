#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <string>

namespace dovahlink::protocol {

/// Encoded response to a successful handshake, exposing the bridge's own compatibility identity
/// to the client.
struct HelloAckPayload {
    /// The DovahLink Bridge/mod release version (matching `bridge/vcpkg.json`'s `version-string`),
    /// for the client to evaluate against its own declared supported range. See
    /// `ai/context/protocol/compatibility.md`.
    std::string bridgeVersion;
    /// Kind of client identity established by this handshake: `"unpaired"` for a session admitted
    /// via `one_time_local_token` or the bootstrap `unpaired` auth method (trust-restricted until
    /// pairing succeeds), or `"paired"` for a session admitted via `trusted_device_credential`, or
    /// a restricted session upgraded in place by a successful pairing confirmation. See
    /// `security.md`'s "Hello authentication and session trust tiers".
    std::string clientIdentityKind;
};

/// Decodes a hello acknowledgment payload.
std::expected<HelloAckPayload, MessageError> DecodeHelloAckPayload(const boost::json::object& payload);

/// Encodes a hello acknowledgment payload.
boost::json::object EncodeHelloAckPayload(const HelloAckPayload& payload);

}  // namespace dovahlink::protocol
