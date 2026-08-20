#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <string>

namespace dovahlink::protocol {

/// Client request to rename itself, per `security.md`'s trust-tier design: only a `kTrusted`
/// device on a Full session may send this.
struct RenameRequestPayload {
    /// The new presentation-only label. Empty clears the device's display name; a non-empty value
    /// replaces it, subject to the trust store's length and control-character validation.
    std::string displayName;
};

/// Decodes a rename request payload.
std::expected<RenameRequestPayload, MessageError> DecodeRenameRequestPayload(const boost::json::object& payload);

}  // namespace dovahlink::protocol
