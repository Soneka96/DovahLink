#pragma once

#include <cstdint>
#include <optional>

#include "capture/character_identity_capture.hpp"
#include "capture/character_supernatural_traits_capture.hpp"
#include "capture/game_time_capture.hpp"
#include "capture/player_location_capture.hpp"

namespace dovahlink::adapter::runtime {

///  One coherent player-vitals read: current and maximum health, magicka, and
///  stamina from a single `RE::PlayerCharacter` lookup, per
///  `roadmap/04-live-state-synchronization-foundation.md`'s "Real capture and
///  host integration" narrow first slice, which requires one coherent fast
///  sample rather than three independently scheduled reads.
struct CharacterVitalsCapture {
    float health = 0.0f;
    float magicka = 0.0f;
    float stamina = 0.0f;
    ///  The effective maximum Health actor value.
    float healthMax = 0.0f;
    ///  The effective maximum Magicka actor value.
    float magickaMax = 0.0f;
    ///  The effective maximum Stamina actor value.
    float staminaMax = 0.0f;

    ///  Structural equality over every field.
    bool operator==(const CharacterVitalsCapture&) const = default;
};

///  Reads current and effective maximum health, magicka, and stamina from one
///  player lookup. Current values use
///  `RE::ActorValueOwner::GetActorValue` -- the current-value accessor, not
///  the permanent/base variants, matching the current-value semantics of the
///  `character_vitals` domain. See `ai/context/adapter/architecture.md` for the
///  maintainer-reviewable record of this choice, including its known open
///  question around death/essential/negative-health behavior.
///  Maximum values use `RE::Actor::GetActorValueMax`, whose CommonLibSSE-NG
///  implementation combines permanent actor value and the temporary modifier.
///  All six values are captured in one call. Call this function on the Skyrim
///  game thread, matching every other approved native read in this codebase.
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

///  Reads the player's display name and identity race as one complete
///  observation. The strings are copied into bounded owned UTF-8 storage.
///  Must be called already on the Skyrim game thread.
///  @return The complete identity, or `std::nullopt` if the player, display
///  name, `charGenRace`, or race display name is unavailable or unusable.
std::optional<capture::CharacterIdentityCapture> CaptureCharacterIdentity();

///  Copies the player's current cell, both location sources, and its worldspace.
///  The returned value owns all strings and may cross the bounded capture handoff.
///  Must be called on the Skyrim game thread.
///  @return The complete required cell context, or `std::nullopt` when it is unavailable.
std::optional<capture::PlayerLocationCapture> CapturePlayerLocation();

///  Reads the player's Skyrim calendar from its backing globals and copies the localized month name.
///  CommonLib's computed date/time accessors are not used as fallbacks for missing globals.
///  Must be called on the Skyrim game thread.
///  @return The complete calendar facts, or `std::nullopt` when the player, Calendar, globals, or month name are unavailable.
std::optional<capture::GameTimeCapture> CaptureGameTime();

///  Reads the vampire global and both transformation spell capabilities as
///  one complete observation. Must be called already on the Skyrim game
///  thread.
///  @return All three independent predicates, or `std::nullopt` if the player,
///  required global, plugin-qualified form, or Beast Form default object is
///  unavailable.
std::optional<capture::CharacterSupernaturalTraitsCapture>
CaptureCharacterSupernaturalTraits();

} //  namespace dovahlink::adapter::runtime
