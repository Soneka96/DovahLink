#include "protocol/snapshot_request_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<SnapshotRequestPayload, MessageError>
DecodeSnapshotRequestPayload(const boost::json::object& payload) {
    auto stateArea =
        DecodeNonEmptyString(RequireField(payload, "stateArea"), "stateArea");
    if (!stateArea) {
        return std::unexpected(stateArea.error());
    }

    std::optional<std::int64_t> knownRevision;
    if (const boost::json::value* knownRevisionValue =
            RequireField(payload, "knownRevision")) {
        auto decoded = DecodeNonNegativeInt(knownRevisionValue, "knownRevision");
        if (!decoded) {
            return std::unexpected(decoded.error());
        }
        knownRevision = *decoded;
    }

    return SnapshotRequestPayload{.stateArea = std::move(*stateArea),
                                  .knownRevision = knownRevision};
}

} //  namespace dovahlink::protocol
