#pragma once

#include "protocol/messages.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <optional>
#include <string>

namespace dovahlink::application {

// The application's own owned representation of the character state area,
// captured through the game-state adapter. Distinct from
// protocol::CharacterState (bridge/protocol/messages.hpp), which decodes the
// wire shape; per ai/context/protocol/conventions.md, application values and
// protocol messages are never the same type even when they represent the
// same concept.
//
// health, magicka, and stamina have no field here: Phase 1 never captures
// them (TASK.md), so the protocol mapping below always publishes them as
// null rather than the application layer carrying a permanently-unset
// placeholder for each. level is optional because it can be unavailable at
// capture time (e.g. before a save is loaded).
struct CharacterSnapshot {
    std::optional<std::int64_t> level;
};

// Builds the character state area's `data` object: level from `snapshot`
// (null if unavailable), and health/magicka/stamina always null in Phase 1.
boost::json::object BuildCharacterStateData(const CharacterSnapshot& snapshot);

// Builds a complete state_snapshot payload for the character state area at
// `revision`, captured at `occurredAt` (RFC 3339 wall-clock text; not an
// ordering source -- see protocol/schema/README.md). The caller supplies the
// revision: this function does not track state across calls (revision
// sequencing is a later, stateful step).
protocol::StateSnapshotPayload BuildCharacterSnapshotPayload(const CharacterSnapshot& snapshot,
                                                              std::int64_t revision,
                                                              std::string occurredAt);

// Builds a complete state_event payload for the character state area from
// `baseRevision` to `revision`, per protocol/schema/README.md's rule that v1
// events carry the complete post-change state, not a patch.
protocol::StateEventPayload BuildCharacterEventPayload(const CharacterSnapshot& snapshot,
                                                        std::int64_t baseRevision,
                                                        std::int64_t revision,
                                                        std::string occurredAt);

}  // namespace dovahlink::application
