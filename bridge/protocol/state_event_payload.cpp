#include "protocol/state_event_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<StateEventPayload, MessageError>
DecodeStateEventPayload(const boost::json::object& payload) {
    auto stateArea =
        DecodeNonEmptyString(RequireField(payload, "stateArea"), "stateArea");
    if (!stateArea) {
        return std::unexpected(stateArea.error());
    }
    auto baseRevision = DecodeNonNegativeInt(
        RequireField(payload, "baseRevision"), "baseRevision");
    if (!baseRevision) {
        return std::unexpected(baseRevision.error());
    }
    auto revision =
        DecodeNonNegativeInt(RequireField(payload, "revision"), "revision");
    if (!revision) {
        return std::unexpected(revision.error());
    }
    auto occurredAt =
        DecodeNonEmptyString(RequireField(payload, "occurredAt"), "occurredAt");
    if (!occurredAt) {
        return std::unexpected(occurredAt.error());
    }
    const boost::json::value* dataValue = RequireField(payload, "data");
    if (!dataValue || !dataValue->is_object()) {
        return Fail("data must be an object");
    }

    return StateEventPayload{
        .stateArea = std::move(*stateArea),
        .baseRevision = *baseRevision,
        .revision = *revision,
        .occurredAt = std::move(*occurredAt),
        .data = dataValue->get_object(),
    };
}

boost::json::object EncodeStateEventPayload(const StateEventPayload& payload) {
    boost::json::object obj;
    obj["stateArea"] = payload.stateArea;
    obj["baseRevision"] = payload.baseRevision;
    obj["revision"] = payload.revision;
    obj["occurredAt"] = payload.occurredAt;
    obj["data"] = payload.data;
    return obj;
}

} //  namespace dovahlink::protocol
