#include "RE/Skyrim.h"

#ifdef GetObject
#undef GetObject
#endif

#include "runtime/commonlib_adapter_character_capture.hpp"

#include <cmath>
#include <optional>
#include <string>
#include <string_view>

#include "constants.hpp"

namespace dovahlink::adapter::runtime {

namespace {

///  Makes a bounded view over a runtime-owned NUL-terminated name.
///  @param value The runtime string pointer.
///  @return A non-empty view of at most the approved byte limit, or
///  `std::nullopt` for a null, empty, or oversized string.
std::optional<std::string_view> TryMakeIdentityStringView(const char* value) {
    if (value == nullptr) {
        return std::nullopt;
    }
    for (std::size_t length = 0; length <= capture::kMaxCharacterIdentityStringBytes; ++length) {
        if (value[length] == '\0') {
            if (length == 0) {
                return std::nullopt;
            }
            return std::string_view(value, length);
        }
    }
    return std::nullopt;
}

///  Makes a bounded view over an optional player-location display name.
///  @param value The runtime-owned NUL-terminated name.
///  @return A non-empty name within the location byte limit, or `std::nullopt` when absent or too long.
std::optional<std::string_view> TryMakePlayerLocationStringView(const char* value) {
    if (value == nullptr) {
        return std::nullopt;
    }
    for (std::size_t length = 0; length <= capture::kMaxPlayerLocationNameBytes; ++length) {
        if (value[length] == '\0') {
            if (length == 0) {
                return std::nullopt;
            }
            return std::string_view(value, length);
        }
    }
    return std::nullopt;
}

} //  namespace

std::optional<CharacterVitalsCapture> CaptureCharacterVitals() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (!player) {
        return std::nullopt;
    }
    auto* actorValues = player->AsActorValueOwner();
    if (!actorValues) {
        return std::nullopt;
    }
    return CharacterVitalsCapture{
        .health = actorValues->GetActorValue(RE::ActorValue::kHealth),
        .magicka = actorValues->GetActorValue(RE::ActorValue::kMagicka),
        .stamina = actorValues->GetActorValue(RE::ActorValue::kStamina),
        .healthMax = player->GetActorValueMax(RE::ActorValue::kHealth),
        .magickaMax = player->GetActorValueMax(RE::ActorValue::kMagicka),
        .staminaMax = player->GetActorValueMax(RE::ActorValue::kStamina),
    };
}

std::optional<float> CaptureCharacterXp() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (!player) {
        return std::nullopt;
    }
    auto* skills = player->GetInfoRuntimeData().skills;
    if (!skills || !skills->data) {
        return std::nullopt;
    }
    return skills->data->xp;
}

std::optional<std::uint16_t> CaptureCharacterLevel() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (!player) {
        return std::nullopt;
    }
    return player->GetLevel();
}

std::optional<capture::CharacterIdentityCapture> CaptureCharacterIdentity() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (player == nullptr) {
        return std::nullopt;
    }

    const auto name = TryMakeIdentityStringView(player->GetDisplayFullName());
    auto* identityRace = player->GetRaceData().charGenRace;
    if (!name || identityRace == nullptr) {
        return std::nullopt;
    }

    const auto raceName = TryMakeIdentityStringView(identityRace->GetFullName());
    if (!raceName) {
        return std::nullopt;
    }
    return capture::TryMakeCharacterIdentityCapture(*name, *raceName);
}

std::optional<capture::PlayerLocationCapture> CapturePlayerLocation() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    auto* cell = player == nullptr ? nullptr : player->GetParentCell();
    if (cell == nullptr) {
        return std::nullopt;
    }

    auto* playerLocation = player->GetPlayerRuntimeData().currentLocation;
    auto* cellLocation = cell->GetLocation();
    auto* worldspace = cell->GetRuntimeData().worldSpace;
    return capture::TryMakePlayerLocationCapture(
        cell->GetFormID(),
        cell->IsInteriorCell(),
        TryMakePlayerLocationStringView(cell->GetFullName()),
        playerLocation == nullptr ? 0 : playerLocation->GetFormID(),
        playerLocation == nullptr ? std::nullopt : TryMakePlayerLocationStringView(playerLocation->GetFullName()),
        cellLocation == nullptr ? 0 : cellLocation->GetFormID(),
        cellLocation == nullptr ? std::nullopt : TryMakePlayerLocationStringView(cellLocation->GetFullName()),
        worldspace == nullptr ? 0 : worldspace->GetFormID(),
        worldspace == nullptr ? std::nullopt : TryMakePlayerLocationStringView(worldspace->GetFullName()));
}

std::optional<capture::GameTimeCapture> CaptureGameTime() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    auto* calendar = RE::Calendar::GetSingleton();
    if (player == nullptr || calendar == nullptr) {
        return std::nullopt;
    }

    auto* year = calendar->gameYear;
    auto* month = calendar->gameMonth;
    auto* day = calendar->gameDay;
    auto* hour = calendar->gameHour;
    if (year == nullptr || month == nullptr || day == nullptr || hour == nullptr ||
        year->type != RE::TESGlobal::Type::kFloat ||
        month->type != RE::TESGlobal::Type::kFloat ||
        day->type != RE::TESGlobal::Type::kFloat ||
        hour->type != RE::TESGlobal::Type::kFloat) {
        return std::nullopt;
    }

    const float rawMonth = month->value;
    if (!std::isfinite(rawMonth) || rawMonth < 0.0f ||
        rawMonth >= static_cast<float>(RE::Calendar::Months::kTotal) ||
        std::trunc(rawMonth) != rawMonth) {
        return std::nullopt;
    }

    const std::string monthName = calendar->GetMonthName();
    return capture::TryMakeGameTimeCapture(
        year->value,
        rawMonth,
        day->value,
        hour->value,
        monthName);
}

std::optional<capture::CharacterSupernaturalTraitsCapture>
CaptureCharacterSupernaturalTraits() {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (player == nullptr) {
        return std::nullopt;
    }

    auto* defaultObjects = RE::BGSDefaultObjectManager::GetSingleton();
    auto* dataHandler = RE::TESDataHandler::GetSingleton();
    if (defaultObjects == nullptr || dataHandler == nullptr) {
        return std::nullopt;
    }

    auto** vampireGlobalSlot = defaultObjects->GetObject<RE::TESGlobal>(
        RE::DefaultObjectID::kPlayerIsVampireVariable);
    auto* vampireLordSpell = dataHandler->LookupForm<RE::SpellItem>(
        0x0283B, "Dawnguard.esm");
    auto** werewolfSpellSlot = defaultObjects->GetObject<RE::SpellItem>(
        RE::DefaultObjectID::kWerewolfSpell);
    auto* vampireGlobal = vampireGlobalSlot == nullptr ? nullptr : *vampireGlobalSlot;
    auto* werewolfSpell = werewolfSpellSlot == nullptr ? nullptr : *werewolfSpellSlot;
    if (vampireGlobal == nullptr || vampireLordSpell == nullptr ||
        werewolfSpell == nullptr) {
        return std::nullopt;
    }

    return capture::CharacterSupernaturalTraitsCapture{
        .isVampire = vampireGlobal->value != 0.0f,
        .hasVampireLordForm = player->HasSpell(vampireLordSpell),
        .hasWerewolfForm = player->HasSpell(werewolfSpell),
    };
}

} //  namespace dovahlink::adapter::runtime
