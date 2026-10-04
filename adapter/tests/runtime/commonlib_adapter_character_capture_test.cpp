#include "capture/character_identity_capture.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>

using dovahlink::adapter::capture::CharacterIdentityCapture;
using dovahlink::adapter::capture::kMaxCharacterIdentityStringBytes;
using dovahlink::adapter::capture::TryEncodeCharacterIdentityPayload;
using dovahlink::adapter::capture::TryMakeCharacterIdentityCapture;
using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("CommonLibAdapterCharacterCapture's vitals read current values and "
          "effective maximums from one coherent player capture",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    //  The test target intentionally does not link CommonLibSSE-NG, and
    //  RE::PlayerCharacter::GetSingleton()'s relocated-pointer resolution is
    //  unsafe to exercise outside a real Skyrim process, so this pins the
    //  architecture-record choice in ai/context/adapter/architecture.md
    //  (GetActorValue, the "current value" accessor) as a source-text
    //  invariant instead of a runtime assertion, so a later edit cannot
    //  silently swap in a different accessor.
    std::string source =
        ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE);

    CHECK(source.find("GetActorValue(RE::ActorValue::kHealth)") != std::string::npos);
    CHECK(source.find("GetActorValue(RE::ActorValue::kMagicka)") != std::string::npos);
    CHECK(source.find("GetActorValue(RE::ActorValue::kStamina)") != std::string::npos);
    CHECK(source.find("GetActorValueMax(RE::ActorValue::kHealth)") != std::string::npos);
    CHECK(source.find("GetActorValueMax(RE::ActorValue::kMagicka)") != std::string::npos);
    CHECK(source.find("GetActorValueMax(RE::ActorValue::kStamina)") != std::string::npos);
    auto captureStart = source.find("CaptureCharacterVitals() {");
    auto xpCaptureStart = source.find("CaptureCharacterXp() {", captureStart);
    REQUIRE(captureStart != std::string::npos);
    REQUIRE(xpCaptureStart != std::string::npos);
    std::string_view vitalsCapture = std::string_view(source).substr(
        captureStart, xpCaptureStart - captureStart);
    auto playerLookup = vitalsCapture.find("RE::PlayerCharacter::GetSingleton()");
    REQUIRE(playerLookup != std::string_view::npos);
    CHECK(vitalsCapture.find("RE::PlayerCharacter::GetSingleton()",
                             playerLookup + 1) == std::string_view::npos);
    CHECK(source.find("GetPermanentActorValue") == std::string::npos);
    CHECK(source.find("GetBaseActorValue") == std::string::npos);
    CHECK(source.find("GetClampedActorValue") == std::string::npos);
}

TEST_CASE("CommonLibAdapterCharacterCapture never returns a fabricated "
          "default when a capture is unavailable",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    //  Guards specifically against the mistake
    //  roadmap/04-live-state-synchronization-foundation.md names by example:
    //  a missing skills pointer must produce std::nullopt (Unavailable), not
    //  a fabricated 0 -- 0 is itself a valid real XP value.
    std::string source =
        ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE);

    CHECK(source.find("return 0.0f;") == std::string::npos);
    CHECK(source.find("return 0;") == std::string::npos);
    CHECK(source.find("return std::nullopt;") != std::string::npos);
}

TEST_CASE("CommonLibAdapterCharacterCapture guards every player-dependent "
          "read behind its own null check, not a shared/skipped one",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    //  Pins the specific guard-then-return-nullopt shape for each of the
    //  three reads, so a later edit cannot silently drop one function's own
    //  null check while leaving the others' intact.
    std::string source =
        NormalizeWhitespace(ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE));

    CHECK(source.find(NormalizeWhitespace(
              "if (!actorValues) {\n        return std::nullopt;\n    }")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "if (!skills || !skills->data) {\n        return std::nullopt;\n    }")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterCharacterCapture reads XP and level from their "
          "documented native paths",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    //  Pins the exact documented native accessors -- GetInfoRuntimeData()'s
    //  skills->data->xp, and Actor::GetLevel() -- against a later edit
    //  silently substituting a different field or accessor.
    std::string source =
        NormalizeWhitespace(ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE));

    CHECK(source.find(NormalizeWhitespace("GetInfoRuntimeData().skills")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("skills->data->xp")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("player->GetLevel();")) != std::string::npos);
}

TEST_CASE("Character identity capture reads the approved player name and "
          "identity race sources as one complete observation",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    std::string source = NormalizeWhitespace(
        ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE));
    const auto identityStart = source.find(NormalizeWhitespace(
        "CaptureCharacterIdentity() {"));
    REQUIRE(identityStart != std::string::npos);
    const std::string_view identitySource = std::string_view(source).substr(identityStart);

    CHECK(identitySource.find("RE::PlayerCharacter::GetSingleton()") != std::string_view::npos);
    CHECK(identitySource.find("player->GetDisplayFullName()") != std::string_view::npos);
    CHECK(identitySource.find("player->GetRaceData().charGenRace") != std::string_view::npos);
    CHECK(identitySource.find("identityRace->GetFullName()") != std::string_view::npos);
    CHECK(identitySource.find(NormalizeWhitespace(
              "if (player == nullptr) { return std::nullopt; }")) !=
          std::string_view::npos);
    CHECK(identitySource.find(NormalizeWhitespace(
              "if (!name || identityRace == nullptr) { return std::nullopt; }")) !=
          std::string_view::npos);
    CHECK(identitySource.find(NormalizeWhitespace(
              "if (!raceName) { return std::nullopt; }")) !=
          std::string_view::npos);
    CHECK(identitySource.find(NormalizeWhitespace(
              "TryMakeCharacterIdentityCapture(*name, *raceName)")) !=
          std::string_view::npos);
    CHECK(identitySource.find("GetRace()") == std::string_view::npos);
    CHECK(identitySource.find("race2") == std::string_view::npos);
}

TEST_CASE("Character identity payload round-trips both UTF-8 strings without "
          "losing the complete observation",
          "[runtime][character_identity_capture]") {
    const std::string name = "Gon\xC3\xA7"
                             "alo";
    const std::string race = "N\xC3\xB6rd";
    const auto identity = TryMakeCharacterIdentityCapture(name, race);
    REQUIRE(identity.has_value());

    const auto payload = TryEncodeCharacterIdentityPayload(*identity);
    REQUIRE(payload.has_value());
    REQUIRE(payload->size == 1 + name.size() + 1 + race.size());

    const auto bytes = payload->AsSpan();
    std::size_t offset = 0;
    const auto encodedNameLength = std::to_integer<std::uint8_t>(bytes[offset++]);
    REQUIRE(encodedNameLength == name.size());
    CHECK(std::string(reinterpret_cast<const char*>(bytes.data() + offset), encodedNameLength) == name);
    offset += encodedNameLength;
    const auto encodedRaceLength = std::to_integer<std::uint8_t>(bytes[offset++]);
    REQUIRE(encodedRaceLength == race.size());
    CHECK(std::string(reinterpret_cast<const char*>(bytes.data() + offset), encodedRaceLength) == race);
    CHECK(offset + encodedRaceLength == bytes.size());
}

TEST_CASE("Character identity fields accept the exact byte limit and reject "
          "oversized values without truncation",
          "[runtime][character_identity_capture]") {
    const std::string maximum(kMaxCharacterIdentityStringBytes, 'x');
    const std::string oversized(kMaxCharacterIdentityStringBytes + 1, 'x');

    const auto exact = TryMakeCharacterIdentityCapture(maximum, maximum);
    REQUIRE(exact.has_value());
    CHECK(exact->nameLength == kMaxCharacterIdentityStringBytes);
    CHECK(exact->raceLength == kMaxCharacterIdentityStringBytes);
    const auto payload = TryEncodeCharacterIdentityPayload(*exact);
    REQUIRE(payload.has_value());
    CHECK(payload->size == 254);

    CHECK_FALSE(TryMakeCharacterIdentityCapture(oversized, "Nord").has_value());
    CHECK_FALSE(TryMakeCharacterIdentityCapture("Goncalo", oversized).has_value());
}

TEST_CASE("Character identity capture rejects empty or malformed UTF-8 text "
          "and malformed encoded lengths",
          "[runtime][character_identity_capture]") {
    CHECK_FALSE(TryMakeCharacterIdentityCapture("", "Nord").has_value());
    CHECK_FALSE(TryMakeCharacterIdentityCapture("Goncalo", "").has_value());
    for (std::string_view invalidText : {
             std::string_view("\xC3", 1),
             std::string_view("\xC0\xAF", 2),
             std::string_view("\xE2\x28\xA1", 3),
             std::string_view("\xED\xA0\x80", 3),
             std::string_view("\xF4\x90\x80\x80", 4),
             std::string_view("\xE2\x82", 2),
             std::string_view("\xF0\x9F\x92", 3),
             std::string_view("a\0b", 3),
         }) {
        CHECK_FALSE(TryMakeCharacterIdentityCapture(invalidText, "Nord").has_value());
    }

    CharacterIdentityCapture malformed;
    auto validIdentity = TryMakeCharacterIdentityCapture("Goncalo", "Nord");
    REQUIRE(validIdentity.has_value());
    malformed = *validIdentity;
    malformed.nameLength = 0;
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(malformed).has_value());
    malformed = *validIdentity;
    malformed.raceLength = 0;
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(malformed).has_value());
    malformed = *validIdentity;
    malformed.nameLength = static_cast<std::uint8_t>(kMaxCharacterIdentityStringBytes + 1);
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(malformed).has_value());
    malformed = *validIdentity;
    malformed.raceLength = static_cast<std::uint8_t>(kMaxCharacterIdentityStringBytes + 1);
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(malformed).has_value());

    auto invalidUtf8Name = *validIdentity;
    invalidUtf8Name.name[0] = static_cast<char>(0x80);
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(invalidUtf8Name).has_value());
    auto invalidUtf8Race = *validIdentity;
    invalidUtf8Race.race[0] = static_cast<char>(0x80);
    CHECK_FALSE(TryEncodeCharacterIdentityPayload(invalidUtf8Race).has_value());
}
