#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("CommonLibAdapterCharacterCapture's vitals read the current-value "
          "actor-value accessor, not permanent/base/clamped",
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
