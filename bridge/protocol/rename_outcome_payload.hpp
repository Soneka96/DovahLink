#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>

#include <expected>
#include <optional>
#include <string>

namespace dovahlink::protocol {

///  Bridge reply to `rename_request`, distinguished by `outcome`.
struct RenameOutcomePayload {
    ///  One of `"renamed"`, `"invalid_display_name"`, `"not_trusted"`.
    ///  `"not_trusted"` covers both an unknown identity and one that is known but
    ///  not currently `kTrusted` -- there is nothing actionable a connected client
    ///  can do differently between those two cases.
    std::string outcome;
    ///  The resulting display name (or absent, if cleared); present only for
    ///  `"renamed"`.
    std::optional<std::string> displayName;
};

///  Decodes a rename outcome payload.
std::expected<RenameOutcomePayload, MessageError>
DecodeRenameOutcomePayload(const boost::json::object& payload);

///  Encodes a rename outcome payload.
boost::json::object
EncodeRenameOutcomePayload(const RenameOutcomePayload& payload);

} //  namespace dovahlink::protocol
