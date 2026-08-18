#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <string>

namespace dovahlink::protocol {

/// Client's final confirmation, echoing back the credential it durably saved -- the wire form of
/// "final confirmation" in `security.md`'s pairing handshake (`PENDING_CREDENTIAL -> TRUSTED`).
struct PairingAckPayload {
    /// Hex-encoded credential the client received in a prior `credential_issued` outcome.
    std::string credential;
};

/// Decodes a pairing ack payload.
std::expected<PairingAckPayload, MessageError> DecodePairingAckPayload(const boost::json::object& payload);

}  // namespace dovahlink::protocol
