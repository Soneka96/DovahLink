#include "protocol/rename_request_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<RenameRequestPayload, MessageError>
DecodeRenameRequestPayload(const boost::json::object& payload) {
    auto displayName =
        DecodeString(RequireField(payload, "displayName"), "displayName");
    if (!displayName) {
        return std::unexpected(displayName.error());
    }
    return RenameRequestPayload{.displayName = std::move(*displayName)};
}

} //  namespace dovahlink::protocol
