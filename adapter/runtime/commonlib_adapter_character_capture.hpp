#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::adapter::runtime {

///  One coherent player-vitals read: health, magicka, and stamina from a
///  single `RE::PlayerCharacter` lookup, per
///  `roadmap/04-live-state-synchronization-foundation.md`'s "Real capture and
///  host integration" narrow first slice, which requires one coherent fast
///  sample rather than three independently scheduled reads.
struct CharacterVitalsCapture {
    float health = 0.0f;
    float magicka = 0.0f;
    float stamina = 0.0f;

    ///  Structural equality over every field.
    bool operator==(const CharacterVitalsCapture&) const = default;
};

///  Reads current health, magicka, and stamina from one player lookup, via
///  `RE::ActorValueOwner::GetActorValue` -- the current-value accessor, not
///  the permanent/base variants, matching `character_health`/
///  `character_magicka`/`character_stamina`'s documented "current value"
///  meaning. See `ai/context/adapter/architecture.md` for the
///  maintainer-reviewable record of this choice, including its known open
///  question around death/essential/negative-health behavior. Must be called
///  already on the Skyrim game thread, matching every other approved native
///  read in this codebase.
///  @return The vitals, or `std::nullopt` if the player or its actor-value
///  owner is not currently available.
std::optional<CharacterVitalsCapture> CaptureCharacterVitals();

///  Reads the player's total unleveled experience points, via
///  `PlayerCharacter::GetInfoRuntimeData().skills->data->xp`. Must be called
///  already on the Skyrim game thread.
///  @return The XP value, or `std::nullopt` if the player or its skills
///  runtime data is not currently available -- never a fabricated `0`, since
///  `0` is itself a valid real XP value.
std::optional<float> CaptureCharacterXp();

///  Reads the player's current level, used only to establish a
///  resynchronization baseline; the live value is otherwise delivered by the
///  `RE::LevelIncrease::Event` sink. Must be called already on the Skyrim
///  game thread.
///  @return The level, or `std::nullopt` if the player is not currently
///  available.
std::optional<std::uint16_t> CaptureCharacterLevel();

} //  namespace dovahlink::adapter::runtime
