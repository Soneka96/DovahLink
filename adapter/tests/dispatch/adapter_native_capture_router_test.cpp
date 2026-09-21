#include "dispatch/adapter_native_capture_router.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <limits>

using dovahlink::adapter::dispatch::AdapterNativeCaptureRouter;
using dovahlink::adapter::dispatch::SampleCaptureStatus;
using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("AdapterNativeCaptureRouter has no registered sample translation "
          "yet, for any sample token",
          "[dispatch][adapter_native_capture_router]") {
    AdapterNativeCaptureRouter router;

    for (std::uint32_t sampleToken :
         {std::uint32_t{0}, std::uint32_t{1}, std::uint32_t{42},
          std::numeric_limits<std::uint32_t>::max()}) {
        CHECK(router.CaptureSample(sampleToken).status ==
              SampleCaptureStatus::kUnsupported);
    }
}

TEST_CASE("AdapterNativeCaptureRouter has no approved event registration "
          "yet, for any event key",
          "[dispatch][adapter_native_capture_router]") {
    AdapterNativeCaptureRouter router;

    for (std::uint32_t eventKey :
         {std::uint32_t{0}, std::uint32_t{1}, std::uint32_t{42},
          std::numeric_limits<std::uint32_t>::max()}) {
        CHECK_FALSE(router.RegisterEvent(eventKey));
    }
}

//  TODO(stage4-file-extraction): Move the CommonLibAdapterNativeCaptureRouter
//  and CommonLibAdapterCharacterCapture structural tests below back to their
//  own tests/runtime/commonlib_adapter_native_capture_router_test.cpp and
//  tests/runtime/commonlib_adapter_character_capture_test.cpp in the
//  post-Stage-4 structural cleanup PR. Temporarily colocated here, with the
//  sibling native-capture-router test, to hold this PR's changed-file count
//  down; extraction only, no behavior change.
namespace {

std::string RouterSource() {
    return ReadSource(DOVAHLINK_ADAPTER_NATIVE_CAPTURE_ROUTER_SOURCE_FILE);
}

} //  namespace

TEST_CASE("CommonLibAdapterNativeCaptureRouter maps each known sample token "
          "to its one approved capture read",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  The test target intentionally does not link CommonLibSSE-NG, so this
    //  pins the token-to-read mapping as a source-text invariant instead of
    //  a runtime assertion.
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kCharacterVitals: {")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureCharacterVitals()")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kCharacterXp: {")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureCharacterXp()")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kCharacterLevelBaseline: {")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureCharacterLevel()")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter reports kUnsupported for an "
          "unknown sample token",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "default:\nreturn dispatch::SampleCaptureResult{\n"
              ".status = dispatch::SampleCaptureStatus::kUnsupported};")) !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter reports kUnavailable, never a "
          "fabricated default, when the underlying capture is unavailable",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  Every arm must report kUnavailable on its own capture's failure, never
    //  fall through to an empty/zeroed payload construction or the
    //  kUnsupported outcome reserved for an unrecognized token.
    std::string source = NormalizeWhitespace(RouterSource());

    for (const char* needle :
         {"if (!vitals) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!xp) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!level) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};"}) {
        CHECK(source.find(NormalizeWhitespace(needle)) != std::string::npos);
    }
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter reports kAvailable with the "
          "encoded payload for each known token's successful read",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = payload};")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = capture::MakeCapturedPayload(encoded)};")) !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter encodes each token's payload "
          "using the shared little-endian codec, at the wire-documented size",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("EncodeFloatLittleEndian")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("EncodeUInt16LittleEndian")) != std::string::npos);
    //  Vitals is 3 float32 fields (12 bytes); the fixed size assignment pins
    //  that a later edit cannot silently add or drop a field without this
    //  test failing.
    CHECK(source.find(NormalizeWhitespace("payload.size = 12;")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter encodes and copies vitals in "
          "health, magicka, stamina order, matching the host's own decode "
          "offsets",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  LiveCaptureSink.cs's ApplyVitals decodes bytes [0,4) as health, [4,8)
    //  as magicka, and [8,12) as stamina; the source's own field and copy
    //  order must match that exactly, not just contain all three fields.
    std::string source = RouterSource();

    auto healthEncodePosition = source.find("EncodeFloatLittleEndian(vitals->health)");
    auto magickaEncodePosition = source.find("EncodeFloatLittleEndian(vitals->magicka)");
    auto staminaEncodePosition = source.find("EncodeFloatLittleEndian(vitals->stamina)");
    REQUIRE(healthEncodePosition != std::string::npos);
    REQUIRE(magickaEncodePosition != std::string::npos);
    REQUIRE(staminaEncodePosition != std::string::npos);
    CHECK(healthEncodePosition < magickaEncodePosition);
    CHECK(magickaEncodePosition < staminaEncodePosition);

    auto healthCopyPosition = source.find("std::ranges::copy(health,");
    auto magickaCopyPosition = source.find("std::ranges::copy(magicka,");
    auto staminaCopyPosition = source.find("std::ranges::copy(stamina,");
    REQUIRE(healthCopyPosition != std::string::npos);
    REQUIRE(magickaCopyPosition != std::string::npos);
    REQUIRE(staminaCopyPosition != std::string::npos);
    CHECK(healthCopyPosition < magickaCopyPosition);
    CHECK(magickaCopyPosition < staminaCopyPosition);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter::RegisterEvent fails closed "
          "for any key other than the approved level-changed key",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "if (static_cast<capture::CharacterEventKey>(eventKey) != "
              "capture::CharacterEventKey::kCharacterLevelChanged) {")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("return false;")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter::RegisterEvent registers the "
          "owned sink instance on RE::LevelIncrease's real event source",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "source->AddEventSink(levelChangedEventSink_.get());")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter::RegisterEvent fails closed "
          "when RE::LevelIncrease's event source is unavailable",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "auto* source = RE::LevelIncrease::GetEventSource();")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("if (source == nullptr) {\nreturn false;")) !=
          std::string::npos);
}

TEST_CASE("LevelChangedEventSink::ProcessEvent guards a null event and "
          "enqueues an Available Event-sourced capture for the level-changed "
          "key on a non-null one",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("if (event == nullptr) {\nreturn "
                                          "RE::BSEventNotifyControl::kContinue;")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("EncodeUInt16LittleEndian(event->newLevel)")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              ".intentKey = static_cast<std::uint32_t>(\n"
              "capture::CharacterEventKey::kCharacterLevelChanged),")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(".source = capture::CaptureSourceKind::kEvent,")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace(".availability = capture::CaptureAvailability::kAvailable,")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              ".capturedValue = capture::MakeCapturedPayload(encoded),")) !=
          std::string::npos);
    //  correlationId stays its captured/no-request default until a following
    //  step adds real resynchronization correlation; guards against it
    //  silently changing here first.
    CHECK(source.find(NormalizeWhitespace(".correlationId = 0,")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("captureQueue_.TryEnqueue(")) != std::string::npos);
}

TEST_CASE("LevelChangedEventSink stamps its capture with the injected "
          "play-context state's current value",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              ".playContextId = playContextState_.CurrentPlayContext().value_or("
              "std::array<std::byte, 16>{}),")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter constructs its owned sink "
          "with the same captureQueue and playContextState it was given",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  Guards against a refactor that constructs the sink with a different
    //  or default-constructed collaborator, silently disconnecting event
    //  captures from the real handoff queue or play-context state.
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace(
              "levelChangedEventSink_(std::make_unique<LevelChangedEventSink>(\n"
              "captureQueue, playContextState))")) != std::string::npos);
}

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
