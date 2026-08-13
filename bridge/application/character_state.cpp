#include "application/character_state.hpp"

#include <boost/json/value.hpp>

#include <utility>

namespace dovahlink::application {

/**
 * @brief Builds JSON state data for a character snapshot.
 *
 * @param snapshot Character snapshot containing the optional level value.
 * @return JSON object containing the character's level, health, magicka, and stamina data.
 */
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

/**
 * @brief Builds a character state snapshot payload.
 *
 * @param snapshot Character snapshot used to generate the payload data.
 * @param revision Revision associated with the snapshot.
 * @param occurredAt Timestamp when the snapshot occurred.
 * @return Character state snapshot payload containing the revision, timestamp, and state data.
 */
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

/**
 * @brief Builds a character state event payload.
 *
 * @param snapshot Character state snapshot used to generate the event data.
 * @param baseRevision Revision from which the event is based.
 * @param revision Revision assigned to the event.
 * @param occurredAt Event occurrence timestamp.
 * @return Character state event payload containing the snapshot data and revision metadata.
 */
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
