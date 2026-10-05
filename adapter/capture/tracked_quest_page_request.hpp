#pragma once

#include <cstdint>

namespace dovahlink::adapter::capture {

///  Maximum tracked quest FormIDs returned in one page.
inline constexpr std::uint16_t kTrackedQuestIdsPerPage = 32;

///  Selects one bounded engine read in a tracked-quest capture cycle.
enum class TrackedQuestPageKind : std::uint8_t {
    ///  A page of currently tracked runtime quest FormIDs.
    kTrackedQuestIds = 0,
    ///  The title and type for one tracked quest.
    kQuestMetadata = 1,
    ///  A page of raw objective instances owned by one tracked quest.
    kObjectives = 2,
};

///  Describes one bounded tracked-quest page request.
struct TrackedQuestPageRequest {
    ///  The page data requested.
    TrackedQuestPageKind kind = TrackedQuestPageKind::kTrackedQuestIds;
    ///  The runtime quest FormID, or zero for a quest-ID page.
    std::uint32_t questId = 0;
    ///  The tracked-ID or raw matching-objective offset for the requested page.
    std::uint16_t cursor = 0;

    ///  Structural equality over every field.
    bool operator==(const TrackedQuestPageRequest&) const = default;
};

///  Checks the bounded argument shape for one quest-page request.
///  @param request The request values to validate.
///  @return Whether its kind, quest ID, and cursor form a supported request.
inline bool IsValidTrackedQuestPageRequest(const TrackedQuestPageRequest& request) {
    switch (request.kind) {
    case TrackedQuestPageKind::kTrackedQuestIds:
        return request.questId == 0 && request.cursor % kTrackedQuestIdsPerPage == 0;
    case TrackedQuestPageKind::kQuestMetadata:
        return request.questId != 0 && request.cursor == 0;
    case TrackedQuestPageKind::kObjectives:
        return request.questId != 0;
    }
    return false;
}

} //  namespace dovahlink::adapter::capture
