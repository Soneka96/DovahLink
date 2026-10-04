#include "capture/character_identity_capture.hpp"
#include "capture/character_supernatural_traits_capture.hpp"
#include "capture/player_location_capture.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <string>
#include <string_view>

using dovahlink::adapter::capture::CharacterIdentityCapture;
using dovahlink::adapter::capture::CharacterSupernaturalTraitsCapture;
using dovahlink::adapter::capture::EncodeCharacterSupernaturalTraitsPayload;
using dovahlink::adapter::capture::kMaxCharacterIdentityStringBytes;
using dovahlink::adapter::capture::kMaxPlayerLocationNameBytes;
using dovahlink::adapter::capture::PlayerLocationCapture;
using dovahlink::adapter::capture::TryEncodeCharacterIdentityPayload;
using dovahlink::adapter::capture::TryEncodePlayerLocationPayload;
using dovahlink::adapter::capture::TryMakeCharacterIdentityCapture;
using dovahlink::adapter::capture::TryMakePlayerLocationCapture;
using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

static_assert(kMaxCharacterIdentityStringBytes == 126);
static_assert(kMaxPlayerLocationNameBytes == 52);

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
    CHECK(payload->size == 2 * (1 + kMaxCharacterIdentityStringBytes));

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

TEST_CASE("Supernatural traits capture uses the approved independent sources "
          "and fails closed when any required source is missing",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    std::string source = NormalizeWhitespace(
        ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE));
    const auto traitsStart = source.find(NormalizeWhitespace(
        "CaptureCharacterSupernaturalTraits() {"));
    REQUIRE(traitsStart != std::string::npos);
    const std::string_view traitsSource = std::string_view(source).substr(traitsStart);

    CHECK(traitsSource.find("RE::PlayerCharacter::GetSingleton()") !=
          std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace(
              "if (player == nullptr) { return std::nullopt; }")) !=
          std::string_view::npos);
    CHECK(traitsSource.find("RE::BGSDefaultObjectManager::GetSingleton()") !=
          std::string_view::npos);
    CHECK(traitsSource.find("RE::TESDataHandler::GetSingleton()") !=
          std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace(
              "if (defaultObjects == nullptr || dataHandler == nullptr) { return std::nullopt; }")) !=
          std::string_view::npos);
    CHECK(traitsSource.find("RE::DefaultObjectID::kPlayerIsVampireVariable") !=
          std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace(
              "auto** vampireGlobalSlot = defaultObjects->GetObject<RE::TESGlobal>(RE::DefaultObjectID::kPlayerIsVampireVariable);")) !=
          std::string_view::npos);
    CHECK(traitsSource.find("*vampireGlobalSlot") != std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace("vampireGlobal->value != 0.0f")) !=
          std::string_view::npos);
    CHECK(traitsSource.find("LookupForm<RE::SpellItem>(0x0283B,\"Dawnguard.esm\")") !=
          std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace(
              "auto** werewolfSpellSlot = defaultObjects->GetObject<RE::SpellItem>(RE::DefaultObjectID::kWerewolfSpell);")) !=
          std::string_view::npos);
    CHECK(traitsSource.find("*werewolfSpellSlot") != std::string_view::npos);
    CHECK(traitsSource.find("player->HasSpell(vampireLordSpell)") !=
          std::string_view::npos);
    CHECK(traitsSource.find("player->HasSpell(werewolfSpell)") !=
          std::string_view::npos);
    CHECK(traitsSource.find("kPlayerIsWerewolfVariable") == std::string_view::npos);
    CHECK(traitsSource.find(NormalizeWhitespace(
              "if (vampireGlobal == nullptr || vampireLordSpell == nullptr || werewolfSpell == nullptr) { return std::nullopt; }")) !=
          std::string_view::npos);
}

TEST_CASE("Supernatural traits encode all eight independent boolean "
          "combinations in public field order",
          "[runtime][character_supernatural_traits_capture]") {
    const std::array<CharacterSupernaturalTraitsCapture, 8> combinations{{
        {.isVampire = false, .hasVampireLordForm = false, .hasWerewolfForm = false},
        {.isVampire = true, .hasVampireLordForm = false, .hasWerewolfForm = false},
        {.isVampire = false, .hasVampireLordForm = true, .hasWerewolfForm = false},
        {.isVampire = false, .hasVampireLordForm = false, .hasWerewolfForm = true},
        {.isVampire = true, .hasVampireLordForm = true, .hasWerewolfForm = false},
        {.isVampire = true, .hasVampireLordForm = false, .hasWerewolfForm = true},
        {.isVampire = false, .hasVampireLordForm = true, .hasWerewolfForm = true},
        {.isVampire = true, .hasVampireLordForm = true, .hasWerewolfForm = true},
    }};

    for (const CharacterSupernaturalTraitsCapture& traits : combinations) {
        const auto payload = EncodeCharacterSupernaturalTraitsPayload(traits);
        REQUIRE(payload.size == 3);
        CHECK(std::ranges::equal(
            payload.AsSpan(),
            std::array<std::byte, 3>{
                static_cast<std::byte>(traits.isVampire ? 1 : 0),
                static_cast<std::byte>(traits.hasVampireLordForm ? 1 : 0),
                static_cast<std::byte>(traits.hasWerewolfForm ? 1 : 0)}));
    }
}

TEST_CASE("Player location copies distinct bounded engine facts and encodes them in field order",
          "[runtime][player_location_capture]") {
    const auto capture = TryMakePlayerLocationCapture(
        0x01000010,
        false,
        "WhiterunWorld",
        0x000A1234,
        "Whiterun",
        0x000A5678,
        "Whiterun",
        0x00000001,
        "Skyrim");
    REQUIRE(capture.has_value());
    CHECK(capture->cellFormId == 0x01000010);
    CHECK_FALSE(capture->cellIsInterior);
    CHECK(capture->playerLocationFormId == 0x000A1234);
    CHECK(capture->cellLocationFormId == 0x000A5678);
    CHECK(capture->worldspaceFormId == 0x00000001);
    CHECK(std::string_view(capture->cellName.data(), capture->cellNameLength) == "WhiterunWorld");
    CHECK(std::string_view(capture->playerLocationName.data(), capture->playerLocationNameLength) == "Whiterun");
    CHECK(std::string_view(capture->cellLocationName.data(), capture->cellLocationNameLength) == "Whiterun");
    CHECK(std::string_view(capture->worldspaceName.data(), capture->worldspaceNameLength) == "Skyrim");

    const auto payload = TryEncodePlayerLocationPayload(*capture);
    REQUIRE(payload.has_value());
    const auto bytes = payload->AsSpan();
    REQUIRE(bytes.size() == 21 + std::string_view("WhiterunWorld").size() +
                                2 * std::string_view("Whiterun").size() +
                                std::string_view("Skyrim").size());
    CHECK(bytes[0] == std::byte{0x10});
    CHECK(bytes[1] == std::byte{0x00});
    CHECK(bytes[2] == std::byte{0x00});
    CHECK(bytes[3] == std::byte{0x01});
    CHECK(bytes[4] == std::byte{0x00});
    CHECK(bytes[5] == std::byte{13});
    CHECK(std::string_view(reinterpret_cast<const char*>(bytes.data() + 6), 13) == "WhiterunWorld");
    CHECK(bytes[23] == std::byte{8});
    CHECK(std::string_view(reinterpret_cast<const char*>(bytes.data() + 24), 8) == "Whiterun");
    CHECK(bytes[36] == std::byte{8});
    CHECK(std::string_view(reinterpret_cast<const char*>(bytes.data() + 37), 8) == "Whiterun");
    CHECK(bytes[49] == std::byte{6});
    CHECK(std::string_view(reinterpret_cast<const char*>(bytes.data() + 50), 6) == "Skyrim");
}

TEST_CASE("Player location keeps a valid unnamed cell available without location or worldspace",
          "[runtime][player_location_capture]") {
    const auto capture = TryMakePlayerLocationCapture(
        0x01000010,
        true,
        std::nullopt,
        0,
        std::nullopt,
        0,
        std::nullopt,
        0,
        std::nullopt);
    REQUIRE(capture.has_value());

    const auto payload = TryEncodePlayerLocationPayload(*capture);
    REQUIRE(payload.has_value());
    CHECK(payload->size == 21);
    CHECK(payload->AsSpan()[4] == std::byte{1});
}

TEST_CASE("Player location preserves localized UTF-8 display names",
          "[runtime][player_location_capture]") {
    const std::string_view localizedCellName = "Monast\xC3\xA8"
                                               "re du lac";
    const std::string_view localizedLocationName =
        "Cr\xC3\xAA"
        "te de l\xE2\x80\x99"
        "ours";
    const auto capture = TryMakePlayerLocationCapture(
        1,
        true,
        localizedCellName,
        2,
        localizedLocationName,
        3,
        localizedLocationName,
        4,
        "Solitude");
    REQUIRE(capture.has_value());

    CHECK(std::string_view(capture->cellName.data(), capture->cellNameLength) == localizedCellName);
    CHECK(std::string_view(capture->playerLocationName.data(), capture->playerLocationNameLength) == localizedLocationName);
    const auto payload = TryEncodePlayerLocationPayload(*capture);
    REQUIRE(payload.has_value());
    CHECK(payload->size > 21);
}

TEST_CASE("Player location names accept the exact bound and omit oversized or malformed optional names",
          "[runtime][player_location_capture]") {
    const std::string maximum(kMaxPlayerLocationNameBytes, 'x');
    const std::string oversized(kMaxPlayerLocationNameBytes + 1, 'x');
    const auto exact = TryMakePlayerLocationCapture(
        1, false, maximum, 2, maximum, 3, maximum, 4, maximum);
    REQUIRE(exact.has_value());
    const auto payload = TryEncodePlayerLocationPayload(*exact);
    REQUIRE(payload.has_value());
    CHECK(payload->size == 4 * sizeof(std::uint32_t) + 1 + 4 * (1 + kMaxPlayerLocationNameBytes));

    const auto oversizedName = TryMakePlayerLocationCapture(
        1, false, oversized, 2, "Location", 3, "Cell location", 4, "Worldspace");
    REQUIRE(oversizedName.has_value());
    CHECK(oversizedName->cellNameLength == 0);

    const auto malformedUtf8 = TryMakePlayerLocationCapture(
        1,
        false,
        std::string_view("\xC3", 1),
        2,
        std::nullopt,
        3,
        std::nullopt,
        4,
        std::nullopt);
    REQUIRE(malformedUtf8.has_value());
    CHECK(malformedUtf8->cellNameLength == 0);

    const auto embeddedNul = TryMakePlayerLocationCapture(
        1,
        false,
        std::string_view("Bad\0name", 8),
        2,
        std::nullopt,
        3,
        std::nullopt,
        4,
        std::nullopt);
    REQUIRE(embeddedNul.has_value());
    CHECK(embeddedNul->cellNameLength == 0);
}

TEST_CASE("Player location rejects missing cells and names without matching runtime FormIDs",
          "[runtime][player_location_capture]") {
    CHECK_FALSE(TryMakePlayerLocationCapture(
                    0, false, std::nullopt, 2, std::nullopt, 3, std::nullopt, 4, std::nullopt)
                    .has_value());
    CHECK_FALSE(TryMakePlayerLocationCapture(
                    1, false, std::nullopt, 0, "Orphaned location", 3, std::nullopt, 4, std::nullopt)
                    .has_value());
    CHECK_FALSE(TryMakePlayerLocationCapture(
                    1, false, std::nullopt, 2, std::nullopt, 0, "Orphaned cell location", 4, std::nullopt)
                    .has_value());
    CHECK_FALSE(TryMakePlayerLocationCapture(
                    1, false, std::nullopt, 2, std::nullopt, 3, std::nullopt, 0, "Orphaned worldspace")
                    .has_value());

    PlayerLocationCapture malformed{};
    malformed.cellFormId = 1;
    malformed.playerLocationFormId = 2;
    malformed.cellLocationFormId = 3;
    malformed.worldspaceFormId = 4;
    const auto oversizedLength = static_cast<std::uint8_t>(kMaxPlayerLocationNameBytes + 1);
    for (int field = 0; field < 4; ++field) {
        PlayerLocationCapture invalid = malformed;
        switch (field) {
        case 0:
            invalid.cellNameLength = oversizedLength;
            break;
        case 1:
            invalid.playerLocationNameLength = oversizedLength;
            break;
        case 2:
            invalid.cellLocationNameLength = oversizedLength;
            break;
        case 3:
            invalid.worldspaceNameLength = oversizedLength;
            break;
        }
        CHECK_FALSE(TryEncodePlayerLocationPayload(invalid).has_value());
    }
}

TEST_CASE("CommonLibAdapterCharacterCapture reads bounded location facts from the current player and cell",
          "[runtime][commonlib_adapter_character_capture][structural]") {
    const std::string source = ReadSource(DOVAHLINK_ADAPTER_CHARACTER_CAPTURE_SOURCE_FILE);
    const std::size_t captureStart = source.find("CapturePlayerLocation() {");
    REQUIRE(captureStart != std::string::npos);
    const std::string_view captureSource = std::string_view(source).substr(captureStart);
    const std::string normalizedCaptureSource = NormalizeWhitespace(captureSource);

    CHECK(captureSource.find("RE::PlayerCharacter::GetSingleton()") != std::string_view::npos);
    CHECK(normalizedCaptureSource.find(NormalizeWhitespace(
              "auto* cell = player == nullptr ? nullptr : player->GetParentCell();")) !=
          std::string::npos);
    CHECK(normalizedCaptureSource.find(NormalizeWhitespace(
              "if (cell == nullptr) { return std::nullopt; }")) != std::string::npos);
    CHECK(captureSource.find("player->GetPlayerRuntimeData().currentLocation") != std::string_view::npos);
    CHECK(captureSource.find("cell->GetLocation()") != std::string_view::npos);
    CHECK(captureSource.find("cell->GetRuntimeData().worldSpace") != std::string_view::npos);
    CHECK(captureSource.find("playerLocation == nullptr ? std::nullopt") != std::string_view::npos);
    CHECK(captureSource.find("cellLocation == nullptr ? std::nullopt") != std::string_view::npos);
    CHECK(captureSource.find("worldspace == nullptr ? std::nullopt") != std::string_view::npos);
    CHECK(captureSource.find("GetParentLocation") == std::string_view::npos);
    CHECK(captureSource.find("TryMakePlayerLocationStringView") != std::string_view::npos);
}
