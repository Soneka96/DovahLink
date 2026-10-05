#include "RE/Skyrim.h"

#include "runtime/commonlib_adapter_tracked_quest_capture.hpp"

#include "capture/tracked_quest_page_codec.hpp"
#include "capture/tracked_quest_page_scanner.hpp"

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
        const auto& quests = dataHandler->GetFormArray<RE::TESQuest>();
        auto payload = capture::ScanTrackedQuestIdsPage(
            quests, request.cursor,
            [](const RE::TESQuest* quest) { return quest != nullptr && quest->IsActive(); },
            [](const RE::TESQuest* quest) { return quest->GetFormID(); });
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

    //  Preserve every engine objective instance owned by this quest. The Host
    //  uses the metadata page's currentInstanceID to select the current set.
    const auto& instances = player->GetPlayerRuntimeData().objectives;
    auto payload = capture::ScanTrackedQuestObjectivesPage(
        quest->GetFormID(), request.cursor, instances,
        [quest](const RE::BGSInstancedQuestObjective& instance) {
            return instance.Objective != nullptr && instance.Objective->ownerQuest == quest;
        },
        [](const RE::BGSInstancedQuestObjective& instance)
            -> std::optional<capture::TrackedQuestObjectiveFact> {
            RE::BGSQuestObjective* objective = instance.Objective;
            std::optional<std::string_view> text;
            if (objective->displayText.data() != nullptr) {
                text = capture::TryMakeTrackedQuestTextView(objective->displayText.c_str(), true);
                if (!text) {
                    return std::nullopt;
                }
            }
            return capture::TrackedQuestObjectiveFact{
                .index = objective->index,
                .instanceId = instance.instanceID,
                .state = static_cast<std::uint8_t>(instance.InstanceState),
                .text = text};
        });
    if (!payload) {
        return UnavailablePage();
    }
    return dispatch::SampleCaptureResult{
        .status = dispatch::SampleCaptureStatus::kAvailable,
        .payload = *payload};
}

} //  namespace dovahlink::adapter::runtime
