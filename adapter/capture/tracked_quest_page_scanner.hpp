#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <optional>
#include <span>

#include "capture/tracked_quest_page_codec.hpp"

namespace dovahlink::adapter::capture {

///  Scans an engine quest range and encodes only the requested tracked-ID page.
///  @tparam QuestRange The engine-owned quest range.
///  @tparam IsTracked Returns whether one quest is tracked by the player.
///  @tparam GetQuestId Returns the quest's runtime FormID.
///  @param quests The engine enumeration to scan without retaining its contents.
///  @param cursor The aligned offset among tracked quests.
///  @param isTracked The Skyrim tracking predicate.
///  @param getQuestId Reads one tracked quest's runtime FormID.
///  @return One encoded page, or `std::nullopt` when a tracked ID or page is invalid.
template <typename QuestRange, typename IsTracked, typename GetQuestId>
std::optional<CapturedPayload> ScanTrackedQuestIdsPage(
    const QuestRange& quests, std::uint16_t cursor, IsTracked isTracked,
    GetQuestId getQuestId) {
    std::array<std::uint32_t, kTrackedQuestIdsPerPage> pageIds{};
    std::size_t trackedOffset = 0;
    std::size_t pageCount = 0;
    bool hasMore = false;

    for (const auto& quest : quests) {
        if (!isTracked(quest)) {
            continue;
        }
        if (trackedOffset++ < cursor) {
            continue;
        }
        if (pageCount == pageIds.size()) {
            hasMore = true;
            break;
        }
        const std::uint32_t questId = getQuestId(quest);
        if (questId == 0) {
            return std::nullopt;
        }
        pageIds[pageCount++] = questId;
    }

    const std::uint32_t nextCursor = static_cast<std::uint32_t>(cursor) +
                                     static_cast<std::uint32_t>(pageCount);
    if (hasMore && nextCursor > (std::numeric_limits<std::uint16_t>::max)()) {
        return std::nullopt;
    }
    return TrackedQuestPageCodec::EncodeQuestIds(
        std::span(pageIds).first(pageCount), hasMore);
}

///  Scans raw quest-owned objective instances and encodes one byte-bounded page.
///  @tparam ObjectiveRange The player's engine objective-instance range.
///  @tparam BelongsToQuest Returns whether an instance is owned by the requested quest.
///  @tparam MakeFact Copies one eligible engine record into a page fact.
///  @param questId The nonzero runtime quest FormID.
///  @param cursor The raw matching-record offset, before Host instance filtering.
///  @param instances The engine enumeration to scan without retaining its contents.
///  @param belongsToQuest The ownership predicate for the requested quest.
///  @param makeFact Reads and validates one included record's engine facts.
///  @return One encoded page, or `std::nullopt` when an included record is invalid.
template <typename ObjectiveRange, typename BelongsToQuest, typename MakeFact>
std::optional<CapturedPayload> ScanTrackedQuestObjectivesPage(
    std::uint32_t questId, std::uint16_t cursor, const ObjectiveRange& instances,
    BelongsToQuest belongsToQuest, MakeFact makeFact) {
    std::array<TrackedQuestObjectiveFact, kTrackedQuestIdsPerPage> facts{};
    std::size_t rawMatchingOffset = 0;
    std::size_t pageCount = 0;
    bool hasMore = false;

    for (const auto& instance : instances) {
        if (!belongsToQuest(instance)) {
            continue;
        }
        if (rawMatchingOffset++ < cursor) {
            continue;
        }
        if (pageCount == facts.size()) {
            hasMore = true;
            break;
        }

        std::optional<TrackedQuestObjectiveFact> fact = makeFact(instance);
        if (!fact) {
            return std::nullopt;
        }
        facts[pageCount] = *fact;

        const std::uint32_t nextCursor = static_cast<std::uint32_t>(cursor) +
                                         static_cast<std::uint32_t>(pageCount) + 1;
        if (nextCursor > (std::numeric_limits<std::uint16_t>::max)()) {
            return std::nullopt;
        }
        auto candidate = TrackedQuestPageCodec::EncodeObjectives(
            questId, static_cast<std::uint16_t>(nextCursor), false,
            std::span(facts).first(pageCount + 1));
        if (!candidate) {
            hasMore = true;
            break;
        }
        ++pageCount;
    }

    const std::uint32_t nextCursor = static_cast<std::uint32_t>(cursor) +
                                     static_cast<std::uint32_t>(pageCount);
    if (nextCursor > (std::numeric_limits<std::uint16_t>::max)()) {
        return std::nullopt;
    }
    return TrackedQuestPageCodec::EncodeObjectives(
        questId, static_cast<std::uint16_t>(nextCursor), hasMore,
        std::span(facts).first(pageCount));
}

} //  namespace dovahlink::adapter::capture
