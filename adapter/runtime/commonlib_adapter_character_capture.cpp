#include "RE/Skyrim.h"

#include "runtime/commonlib_adapter_character_capture.hpp"

namespace dovahlink::adapter::runtime {

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

} //  namespace dovahlink::adapter::runtime
