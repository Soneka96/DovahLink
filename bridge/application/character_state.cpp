#include "application/character_state.hpp"

#include <boost/json/value.hpp>

#include <utility>

namespace dovahlink::application {

boost::json::object BuildCharacterStateData(const CharacterSnapshot& snapshot) {
    boost::json::object data;
    if (snapshot.level.has_value()) {
        data["level"] = *snapshot.level;
    } else {
        data["level"] = nullptr;
    }
    data["health"] = nullptr;
    data["magicka"] = nullptr;
    data["stamina"] = nullptr;
    return data;
}

protocol::StateSnapshotPayload BuildCharacterSnapshotPayload(const CharacterSnapshot& snapshot,
                                                              std::int64_t revision,
                                                              std::string occurredAt) {
    return protocol::StateSnapshotPayload{
        .stateArea = std::string(protocol::state_area::kCharacter),
        .revision = revision,
        .occurredAt = std::move(occurredAt),
        .data = BuildCharacterStateData(snapshot),
    };
}

protocol::StateEventPayload BuildCharacterEventPayload(const CharacterSnapshot& snapshot,
                                                        std::int64_t baseRevision,
                                                        std::int64_t revision,
                                                        std::string occurredAt) {
    return protocol::StateEventPayload{
        .stateArea = std::string(protocol::state_area::kCharacter),
        .baseRevision = baseRevision,
        .revision = revision,
        .occurredAt = std::move(occurredAt),
        .data = BuildCharacterStateData(snapshot),
    };
}

}  // namespace dovahlink::application
