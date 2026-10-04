#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include "runtime/commonlib_world_context_diagnostics.hpp"

#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <iomanip>
#include <sstream>
#include <string>
#include <string_view>
#include <utility>

namespace dovahlink::adapter::runtime {
namespace {

///  Bounds location-parent traversal so malformed data cannot loop forever.
constexpr std::size_t kMaxLocationParentDepth = 8;
///  Bounds the number of quest records printed in one sample.
constexpr std::size_t kMaxQuestEntries = 24;
///  Bounds the number of instantiated objectives printed in one sample.
constexpr std::size_t kMaxObjectiveEntries = 48;
///  Bounds displayed names and objective text to keep diagnostic blocks small.
constexpr std::size_t kMaxTextLength = 120;

///  Escapes a runtime string for a compact, quoted diagnostic field.
///  @param value The runtime text to escape.
///  @return An escaped string no longer than `kMaxTextLength` source bytes.
std::string EscapeText(const char* value) {
    if (value == nullptr || value[0] == '\0') {
        return "";
    }

    std::string result;
    result.reserve(kMaxTextLength);
    for (std::size_t index = 0; value[index] != '\0' && index < kMaxTextLength;
         ++index) {
        const char character = value[index];
        if (character == '"' || character == '\\') {
            result.push_back('\\');
            result.push_back(character);
        } else if (character == '\n' || character == '\r' || character == '\t') {
            result.push_back(' ');
        } else if (static_cast<unsigned char>(character) >= 0x20) {
            result.push_back(character);
        }
    }
    return result;
}

///  Emits one domain block only when its contents differ from the last sample.
///  @param previous The previous emitted block for the domain.
///  @param domain The concise log domain name.
///  @param current The newly formatted block body.
void LogWhenChanged(std::string& previous, std::string domain,
                    std::string current) {
    if (current == previous) {
        return;
    }
    previous = std::move(current);
    SKSE::log::info("[DovahLink][WorldContext][{}]\n{}", domain, previous);
}

///  Formats a location identity without retaining a borrowed Skyrim object.
///  @param location The location whose form identity should be displayed.
///  @return A compact form ID and full name, or `none` for a null location.
std::string FormatLocationIdentity(const RE::BGSLocation* location) {
    if (location == nullptr) {
        return "none";
    }
    const char* fullName = location->GetFullName();
    std::ostringstream output;
    output << "id=0x" << std::uppercase << std::hex << std::setw(8)
           << std::setfill('0') << location->GetFormID() << std::dec
           << " name=\"" << EscapeText(fullName) << "\"";
    return output.str();
}

///  Formats a bounded BGSLocation parent chain and marks repeated nodes.
///  @param first The location at which traversal begins.
///  @return The chain, including an explicit cycle or truncation marker.
std::string FormatLocationParents(const RE::BGSLocation* first) {
    if (first == nullptr) {
        return "[]";
    }

    std::array<const RE::BGSLocation*, kMaxLocationParentDepth> seenPointers{};
    std::array<RE::FormID, kMaxLocationParentDepth> seenFormIds{};
    seenPointers[0] = first;
    seenFormIds[0] = first->GetFormID();
    std::size_t seenCount = 1;
    const RE::BGSLocation* current = first->parentLoc;
    std::ostringstream output;
    output << '[';
    while (current != nullptr && seenCount < kMaxLocationParentDepth) {
        const RE::FormID formId = current->GetFormID();
        bool repeated = false;
        for (std::size_t index = 0; index < seenCount; ++index) {
            if (seenPointers[index] == current || seenFormIds[index] == formId) {
                repeated = true;
                break;
            }
        }
        if (seenCount > 1) {
            output << ", ";
        }
        if (repeated) {
            output << "cycle:" << FormatLocationIdentity(current);
            current = nullptr;
            break;
        }

        seenPointers[seenCount] = current;
        seenFormIds[seenCount] = formId;
        ++seenCount;
        output << FormatLocationIdentity(current);
        current = current->parentLoc;
    }
    if (current != nullptr) {
        if (seenCount != 0) {
            output << ", ";
        }
        output << "truncated after " << kMaxLocationParentDepth;
    }
    output << ']';
    return output.str();
}

///  Formats the player's cell and both location candidates for comparison.
///  @param player The current player, or null when Skyrim has none.
///  @return A compact location diagnostic block.
std::string FormatLocationSample(const RE::PlayerCharacter* player) {
    std::ostringstream output;
    output << "playerExists=" << (player != nullptr ? "true" : "false") << '\n';
    if (player == nullptr) {
        output << "cell=none\nplayerLocation=none\ncellLocation=none\nworldspace=none";
        return output.str();
    }

    RE::TESObjectCELL* cell = player->GetParentCell();
    const RE::BGSLocation* playerLocation =
        player->GetPlayerRuntimeData().currentLocation;
    const RE::BGSLocation* cellLocation =
        cell != nullptr ? cell->GetLocation() : nullptr;

    output << "cell:\n";
    if (cell == nullptr) {
        output << "  none\n";
    } else {
        const char* fullName = cell->GetFullName();
        output << "  id=0x" << std::uppercase << std::hex << std::setw(8)
               << std::setfill('0') << cell->GetFormID() << std::dec
               << " kind=" << (cell->IsInteriorCell() ? "interior" : "exterior")
               << " name=\"" << EscapeText(fullName) << "\"\n";
        if (cell->IsExteriorCell()) {
            const RE::EXTERIOR_DATA* coordinates = cell->GetCoordinates();
            if (coordinates != nullptr) {
                output << "  grid=" << coordinates->cellX << ',' << coordinates->cellY
                       << '\n';
            }
        }
    }

    output << "playerLocation: " << FormatLocationIdentity(playerLocation)
           << " parents=" << FormatLocationParents(playerLocation) << '\n';
    output << "cellLocation: " << FormatLocationIdentity(cellLocation)
           << " parents=" << FormatLocationParents(cellLocation) << '\n';

    const RE::TESWorldSpace* worldspace =
        cell != nullptr ? cell->GetRuntimeData().worldSpace : nullptr;
    if (worldspace == nullptr) {
        output << "worldspace=none";
    } else {
        output << "worldspace: id=0x" << std::uppercase << std::hex
               << std::setw(8) << std::setfill('0') << worldspace->GetFormID()
               << std::dec << " name=\"" << EscapeText(worldspace->GetFullName())
               << '\"';
    }
    return output.str();
}

///  Formats quest targets and instantiated objectives while holding Skyrim's
///  associated quest-target lock.
///  @param player The current player, or null when Skyrim has none.
///  @return A bounded block of raw quest and objective facts.
std::string FormatQuestSample(RE::PlayerCharacter* player) {
    std::ostringstream output;
    if (player == nullptr) {
        output << "playerExists=false\nquestTargets=unavailable\nobjectives=unavailable";
        return output.str();
    }

    auto& runtimeData = player->GetPlayerRuntimeData();
    RE::BSSpinLockGuard lock(runtimeData.questTargetsLock);
    output << "playerExists=true\nquestTargetsTotal="
           << runtimeData.questTargets.size() << "\nobjectivesTotal="
           << runtimeData.objectives.size()
           << "\nfocusedQuestField=not identified\n";

    std::size_t questCount = 0;
    for (const auto& entry : runtimeData.questTargets) {
        if (questCount >= kMaxQuestEntries) {
            break;
        }
        const RE::TESQuest* quest = entry.first;
        const auto* targets = entry.second;
        output << "quest:\n  id=";
        if (quest == nullptr) {
            output << "none\n  editorId=none\n  title=none\n";
        } else {
            const char* editorId = quest->GetFormEditorID();
            const char* title = quest->GetFullName();
            output << "0x" << std::uppercase << std::hex << std::setw(8)
                   << std::setfill('0') << quest->GetFormID() << std::dec
                   << "\n  editorId=\"" << EscapeText(editorId)
                   << "\"\n  title=\"" << EscapeText(title) << "\"\n"
                   << "  activeFlag="
                   << (quest->data.flags.any(RE::QuestFlag::kActive) ? "true"
                                                                     : "false")
                   << " activeMethod="
                   << (quest->IsActive() ? "true" : "false") << " flags=0x"
                   << std::uppercase << std::hex
                   << quest->data.flags.underlying() << std::dec << '\n';
        }
        output << "  appearsInQuestTargets=true\n  targetCount="
               << (targets != nullptr ? targets->size() : 0) << '\n';
        ++questCount;
    }
    if (runtimeData.questTargets.size() > questCount) {
        output << "questEntriesTruncated=true\n";
    }

    std::size_t questLogCount = 0;
    bool questLogTruncated = false;
    for (auto entry = runtimeData.questLog.begin();
         entry != runtimeData.questLog.end(); ++entry) {
        if (questLogCount >= kMaxQuestEntries) {
            questLogTruncated = true;
            break;
        }
        ++questLogCount;
        auto* logEntry = *entry;
        output << "questLogEntry:\n";
        if (logEntry == nullptr) {
            output << "  none\n";
        } else {
            const RE::TESQuest* owner = logEntry->owner;
            if (owner == nullptr) {
                output << "  quest=none\n";
            } else {
                output << "  questId=0x" << std::uppercase << std::hex
                       << std::setw(8) << std::setfill('0') << owner->GetFormID()
                       << std::dec << " title=\""
                       << EscapeText(owner->GetFullName()) << "\"\n";
            }
            output << "  index=" << static_cast<std::uint32_t>(logEntry->index)
                   << " hasLogEntry="
                   << (logEntry->hasLogEntry ? "true" : "false");
            if (logEntry->hasLogEntry && owner != nullptr) {
                output << " text=\""
                       << EscapeText(logEntry->GetLogEntry(owner)) << '\"';
            }
            output << '\n';
        }
    }
    output << "questLogEntriesObserved=" << questLogCount << '\n';
    if (questLogTruncated) {
        output << "questLogEntriesTruncated=true\n";
    }

    std::size_t objectiveCount = 0;
    for (const auto& instance : runtimeData.objectives) {
        if (objectiveCount >= kMaxObjectiveEntries) {
            break;
        }
        ++objectiveCount;
        const RE::BGSQuestObjective* objective = instance.Objective;
        if (objective == nullptr) {
            continue;
        }
        const auto state = instance.InstanceState;
        const bool displayed =
            state == RE::QUEST_OBJECTIVE_STATE::kDisplayed ||
            state == RE::QUEST_OBJECTIVE_STATE::kCompletedDisplayed ||
            state == RE::QUEST_OBJECTIVE_STATE::kFailedDisplayed;
        const bool completed = state == RE::QUEST_OBJECTIVE_STATE::kCompleted ||
                               state == RE::QUEST_OBJECTIVE_STATE::kCompletedDisplayed;
        const bool failed = state == RE::QUEST_OBJECTIVE_STATE::kFailed ||
                            state == RE::QUEST_OBJECTIVE_STATE::kFailedDisplayed;
        const char* text = objective->displayText.c_str();
        const RE::TESQuest* owner = objective->ownerQuest;
        output << "objective:\n  questId=";
        if (owner == nullptr) {
            output << "none\n";
        } else {
            const char* ownerEditorId = owner->GetFormEditorID();
            const char* ownerTitle = owner->GetFullName();
            output << "0x" << std::uppercase << std::hex << std::setw(8)
                   << std::setfill('0') << owner->GetFormID() << std::dec
                   << " editorId=\"" << EscapeText(ownerEditorId)
                   << "\" title=\"" << EscapeText(ownerTitle) << '\"'
                   << "\n  questInTargets="
                   << (runtimeData.questTargets.contains(
                           const_cast<RE::TESQuest*>(owner))
                           ? "true"
                           : "false")
                   << '\n';
        }
        output << "  index=" << objective->index
               << " instanceId=" << instance.instanceID
               << " state=" << std::to_underlying(state)
               << " displayed=" << (displayed ? "true" : "false")
               << " completed=" << (completed ? "true" : "false")
               << " failed=" << (failed ? "true" : "false")
               << " dormant="
               << (state == RE::QUEST_OBJECTIVE_STATE::kDormant ? "true" : "false")
               << " text=\"" << EscapeText(text) << "\"\n";
    }
    if (runtimeData.objectives.size() > objectiveCount) {
        output << "objectiveEntriesTruncated=true\n";
    }
    return output.str();
}

///  Formats Calendar's backing globals and their CommonLib derived values.
///  @return A block that marks missing backing globals before displaying values.
std::string FormatTimeSample() {
    const RE::Calendar* calendar = RE::Calendar::GetSingleton();
    std::ostringstream output;
    if (calendar == nullptr) {
        output << "calendar=none\nbackingGlobalsValid=false\ngetterValues=unavailable";
        return output.str();
    }

    const bool yearValid = calendar->gameYear != nullptr;
    const bool monthValid = calendar->gameMonth != nullptr;
    const bool dayValid = calendar->gameDay != nullptr;
    const bool hourValid = calendar->gameHour != nullptr;
    const bool timescaleValid = calendar->timeScale != nullptr;
    const bool globalsValid = yearValid && monthValid && dayValid && hourValid;
    output << "calendar=present\nbackingGlobalsValid="
           << (globalsValid ? "true" : "false") << " yearGlobal="
           << (yearValid ? "valid" : "missing") << " monthGlobal="
           << (monthValid ? "valid" : "missing") << " dayGlobal="
           << (dayValid ? "valid" : "missing") << " hourGlobal="
           << (hourValid ? "valid" : "missing") << " timeScaleGlobal="
           << (timescaleValid ? "valid" : "missing") << '\n';

    if (!globalsValid) {
        output << "getterValues=skipped; fallback values not trusted";
        return output.str();
    }

    const float fractionalHour = calendar->GetHour();
    if (!std::isfinite(fractionalHour) || fractionalHour < 0.0F ||
        fractionalHour >= RE::Calendar::GetHoursPerDay()) {
        output << "getterValuesSource=validCalendarGlobals fractionalHour="
               << fractionalHour << " derivedTime=invalid";
        return output.str();
    }
    const auto derivedHour = static_cast<std::uint32_t>(std::floor(fractionalHour));
    const auto derivedMinute = static_cast<std::uint32_t>(
        std::floor((fractionalHour - static_cast<float>(derivedHour)) * 60.0F));
    output << "rawYear=" << calendar->gameYear->value
           << " year=" << calendar->GetYear()
           << " rawMonth=" << calendar->gameMonth->value
           << " month=" << calendar->GetMonth()
           << " rawDay=" << calendar->gameDay->value
           << " day=" << calendar->GetDay()
           << " rawHour=" << calendar->gameHour->value
           << " fractionalHour=" << std::fixed << std::setprecision(4)
           << fractionalHour << " derivedHour=" << derivedHour
           << " derivedMinute=" << derivedMinute;
    if (timescaleValid) {
        output << " timescale=" << calendar->timeScale->value;
    } else {
        output << " timescale=unavailable";
    }
    if (calendar->gameDaysPassed != nullptr) {
        output << " rawDaysPassed=" << calendar->gameDaysPassed->value;
    }
    output << " getterValuesSource=validCalendarGlobals";
    return output.str();
}

} //  namespace

void CaptureWorldContextDiagnostics() {
#if !defined(NDEBUG)
    try {
        using clock = std::chrono::steady_clock;
        static clock::time_point lastSample{};
        static std::string previousLocation;
        static std::string previousQuest;
        static std::string previousTime;

        const auto now = clock::now();
        if (lastSample != clock::time_point{} && now - lastSample < std::chrono::seconds(1)) {
            return;
        }
        lastSample = now;

        RE::PlayerCharacter* player = RE::PlayerCharacter::GetSingleton();
        LogWhenChanged(previousLocation, "Location", FormatLocationSample(player));
        LogWhenChanged(previousQuest, "Quest", FormatQuestSample(player));
        LogWhenChanged(previousTime, "Time", FormatTimeSample());
    } catch (...) {
        try {
            SKSE::log::warn("[DovahLink][WorldContext] diagnostic sample failed");
        } catch (...) {
        }
    }
#endif
}

} //  namespace dovahlink::adapter::runtime
