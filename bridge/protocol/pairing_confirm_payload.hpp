#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

/// Client submission of a pairing code, per `security.md`'s pairing handshake
/// (`CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`).
struct PairingConfirmPayload {
  /// The six-digit code the user read from Skyrim and entered.
  std::string code;
  /// Optional presentation-only label for the resulting trusted client.
  std::optional<std::string> displayName;
};

/// Decodes a pairing confirm payload.
std::expected<PairingConfirmPayload, MessageError>
DecodePairingConfirmPayload(const boost::json::object &payload);

} // namespace dovahlink::protocol
