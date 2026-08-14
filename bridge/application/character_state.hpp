#pragma once

#include "protocol/messages.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <optional>
#include <string>

namespace dovahlink::application {

/// Owned application value for the character state area.
struct CharacterSnapshot {
    /// Captured player level, or no value when the runtime value is unavailable.
    std::optional<std::int64_t> level;
};

/// Builds the character state payload data, preserving unavailable values.
/// @param snapshot Captured character state.
boost::json::object BuildCharacterStateData(const CharacterSnapshot& snapshot);

/// Builds a complete character state snapshot payload.
/// @param snapshot Captured character state.
/// @param revision State-area revision assigned by the caller.
/// @param occurredAt RFC 3339 wall-clock timestamp for the snapshot.
/// @return Snapshot payload containing the supplied revision and timestamp.
protocol::StateSnapshotPayload BuildCharacterSnapshotPayload(const CharacterSnapshot& snapshot,
                                                              std::int64_t revision,
                                                              std::string occurredAt);

/// Builds a complete character state event payload.
/// @param snapshot Current character state after the change.
/// @param baseRevision Revision immediately preceding the event.
/// @param revision Revision assigned to the event.
/// @param occurredAt RFC 3339 wall-clock timestamp for the event.
/// @return Event payload containing the complete post-change state.
protocol::StateEventPayload BuildCharacterEventPayload(const CharacterSnapshot& snapshot,
                                                        std::int64_t baseRevision,
                                                        std::int64_t revision,
                                                        std::string occurredAt);

}  // namespace dovahlink::application
