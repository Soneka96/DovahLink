#include "RE/Skyrim.h"

#include "runtime/commonlib_adapter_character_capture.hpp"

#include <optional>
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

} //  namespace dovahlink::adapter::runtime
