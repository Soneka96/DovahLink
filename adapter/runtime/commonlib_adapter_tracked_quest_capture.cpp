#include "RE/Skyrim.h"

#include "runtime/commonlib_adapter_tracked_quest_capture.hpp"

#include "capture/tracked_quest_page_codec.hpp"
#include <array>

namespace dovahlink::adapter::runtime {

namespace {

///  Finds a loaded quest by runtime FormID without retaining the engine pointer.
///  @param questId The requested runtime FormID.
///  @return The matching quest if it is loaded and currently tracked.
RE::TESQuest* FindTrackedQuest(std::uint32_t questId) {
    auto* quest = RE::TESForm::LookupByID<RE::TESQuest>(questId);
    return quest != nullptr && quest->IsActive() ? quest : nullptr;
}

///  Returns a known-unavailable page without substituting an empty page.
dispatch::SampleCaptureResult UnavailablePage() {
    return dispatch::SampleCaptureResult{
        .status = dispatch::SampleCaptureStatus::kUnavailable};
}

} //  namespace

dispatch::SampleCaptureResult CaptureCommonLibTrackedQuestPage(
    const capture::TrackedQuestPageRequest& request) {
    if (!capture::IsValidTrackedQuestPageRequest(request)) {
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kUnsupported};
    }

    if (request.kind == capture::TrackedQuestPageKind::kTrackedQuestIds) {
        auto* dataHandler = RE::TESDataHandler::GetSingleton();
        if (dataHandler == nullptr) {
            return UnavailablePage();
        }
        std::array<std::uint32_t, capture::kMaxTrackedQuests> ids{};
        std::size_t trackedCount = 0;
        for (RE::TESQuest* quest : dataHandler->GetFormArray<RE::TESQuest>()) {
            if (quest == nullptr || !quest->IsActive()) {
                continue;
            }
            if (trackedCount == ids.size()) {
                return UnavailablePage();
            }
            ids[trackedCount++] = quest->GetFormID();
        }
        auto payload = capture::TrackedQuestPageCodec::EncodeQuestIdPage(
            std::span(ids).first(trackedCount), request.cursor);
        if (!payload) {
            return UnavailablePage();
        }
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kAvailable,
            .payload = *payload};
    }

    RE::TESQuest* quest = FindTrackedQuest(request.questId);
    if (quest == nullptr) {
        return UnavailablePage();
    }

    if (request.kind == capture::TrackedQuestPageKind::kQuestMetadata) {
        auto title = capture::TryMakeTrackedQuestTextView(quest->GetFullName(), false);
        if (!title) {
            return UnavailablePage();
        }
        auto payload = capture::TrackedQuestPageCodec::EncodeMetadata(
            quest->GetFormID(), static_cast<std::uint8_t>(quest->GetType()),
            quest->currentInstanceID, *title);
        if (!payload) {
            return UnavailablePage();
        }
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kAvailable,
            .payload = *payload};
    }

    auto* player = RE::PlayerCharacter::GetSingleton();
    if (player == nullptr) {
        return UnavailablePage();
    }

    std::array<capture::TrackedQuestObjectiveFact, 32> facts{};
    std::size_t factCount = 0;
    std::size_t objectiveOffset = 0;
    bool hasMore = false;
    //  Preserve every engine objective instance owned by this quest. The Host
    //  uses the metadata page's currentInstanceID to select the current set.
    for (const RE::BGSInstancedQuestObjective& instance :
         player->GetPlayerRuntimeData().objectives) {
        RE::BGSQuestObjective* objective = instance.Objective;
        if (objective == nullptr || objective->ownerQuest != quest) {
            continue;
        }
        if (!capture::IsTrackedQuestObjectiveCountWithinLimit(objectiveOffset + 1)) {
            return UnavailablePage();
        }
        if (objectiveOffset++ < request.cursor) {
            continue;
        }
        std::optional<std::string_view> text;
        if (objective->displayText.data() != nullptr) {
            text = capture::TryMakeTrackedQuestTextView(objective->displayText.c_str(), true);
            if (!text) {
                return UnavailablePage();
            }
        }
        const auto fact = capture::TrackedQuestObjectiveFact{
            .index = objective->index,
            .instanceId = instance.instanceID,
            .state = static_cast<std::uint8_t>(instance.InstanceState),
            .text = text};
        if (factCount == facts.size()) {
            hasMore = true;
            break;
        }
        facts[factCount] = fact;
        auto fitsPage = capture::TrackedQuestPageCodec::EncodeObjectives(
            quest->GetFormID(),
            static_cast<std::uint16_t>(request.cursor + factCount + 1), false,
            std::span(facts).first(factCount + 1));
        if (!fitsPage) {
            hasMore = true;
            break;
        }
        ++factCount;
    }

    const std::uint16_t nextCursor = static_cast<std::uint16_t>(request.cursor + factCount);
    auto payload = capture::TrackedQuestPageCodec::EncodeObjectives(
        quest->GetFormID(), nextCursor, hasMore,
        std::span(facts).first(factCount));
    if (!payload) {
        return UnavailablePage();
    }
    return dispatch::SampleCaptureResult{
        .status = dispatch::SampleCaptureStatus::kAvailable,
        .payload = *payload};
}

} //  namespace dovahlink::adapter::runtime
