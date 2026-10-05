#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Reads the CommonLib native capture-router implementation for structural assertions.
std::string RouterSource() {
    return ReadSource(DOVAHLINK_ADAPTER_NATIVE_CAPTURE_ROUTER_SOURCE_FILE);
}

} //  namespace

TEST_CASE("CommonLibAdapterNativeCaptureRouter does not emit temporary World Context diagnostics",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    const std::string source = RouterSource();

    CHECK(source.find("commonlib_world_context_diagnostics") == std::string::npos);
    CHECK(source.find("CaptureWorldContextDiagnostics") == std::string::npos);
}

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
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kCharacterIdentity: {")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureCharacterIdentity()")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kCharacterSupernaturalTraits: {")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureCharacterSupernaturalTraits()")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kPlayerLocation: {")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CapturePlayerLocation()")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              "case capture::CharacterSampleToken::kGameTime: {")) !=
          std::string::npos);
    CHECK(source.find(NormalizeWhitespace("CaptureGameTime()")) !=
          std::string::npos);
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
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!identity) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!payload) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!traits) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!location) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!payload) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};",
          "if (!gameTime) {\nreturn dispatch::SampleCaptureResult{\n"
          ".status = dispatch::SampleCaptureStatus::kUnavailable};"}) {
        CHECK(source.find(NormalizeWhitespace(needle)) != std::string::npos);
    }
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter reports kAvailable with the "
          "encoded payload for each known token's successful read",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());
    const auto identityStart = source.find(
        NormalizeWhitespace("case capture::CharacterSampleToken::kCharacterIdentity: {"));
    REQUIRE(identityStart != std::string::npos);
    const auto identityEnd = source.find("default:", identityStart);
    REQUIRE(identityEnd != std::string::npos);
    const std::string_view identityCase =
        std::string_view(source).substr(identityStart, identityEnd - identityStart);

    CHECK(source.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = payload};")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = capture::MakeCapturedPayload(encoded)};")) !=
          std::string::npos);
    CHECK(identityCase.find(NormalizeWhitespace(
              "CaptureCharacterIdentity()")) != std::string_view::npos);
    CHECK(identityCase.find(NormalizeWhitespace(
              "TryEncodeCharacterIdentityPayload(*identity)")) != std::string_view::npos);
    CHECK(identityCase.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = *payload};")) != std::string::npos);
    const auto traitsStart = source.find(NormalizeWhitespace(
        "case capture::CharacterSampleToken::kCharacterSupernaturalTraits: {"));
    REQUIRE(traitsStart != std::string::npos);
    const auto traitsEnd = source.find("default:", traitsStart);
    REQUIRE(traitsEnd != std::string::npos);
    const std::string_view traitsCase =
        std::string_view(source).substr(traitsStart, traitsEnd - traitsStart);
    CHECK(traitsCase.find(NormalizeWhitespace(
              "CaptureCharacterSupernaturalTraits()")) != std::string_view::npos);
    CHECK(traitsCase.find(NormalizeWhitespace(
              "EncodeCharacterSupernaturalTraitsPayload(*traits)")) !=
          std::string_view::npos);
    CHECK(traitsCase.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = capture::EncodeCharacterSupernaturalTraitsPayload(*traits)};")) !=
          std::string_view::npos);

    const auto locationStart = source.find(
        NormalizeWhitespace("case capture::CharacterSampleToken::kPlayerLocation: {"));
    REQUIRE(locationStart != std::string::npos);
    const auto locationEnd = source.find("default:", locationStart);
    REQUIRE(locationEnd != std::string::npos);
    const std::string_view locationCase =
        std::string_view(source).substr(locationStart, locationEnd - locationStart);
    CHECK(locationCase.find(NormalizeWhitespace("CapturePlayerLocation()")) !=
          std::string_view::npos);
    CHECK(locationCase.find(NormalizeWhitespace(
              "TryEncodePlayerLocationPayload(*location)")) != std::string_view::npos);
    CHECK(locationCase.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = *payload};")) != std::string_view::npos);

    const auto gameTimeStart = source.find(
        NormalizeWhitespace("case capture::CharacterSampleToken::kGameTime: {"));
    REQUIRE(gameTimeStart != std::string::npos);
    const auto gameTimeEnd = source.find("default:", gameTimeStart);
    REQUIRE(gameTimeEnd != std::string::npos);
    const std::string_view gameTimeCase =
        std::string_view(source).substr(gameTimeStart, gameTimeEnd - gameTimeStart);
    CHECK(gameTimeCase.find(NormalizeWhitespace("CaptureGameTime()")) !=
          std::string_view::npos);
    CHECK(gameTimeCase.find(NormalizeWhitespace(
              "TryEncodeGameTimePayload(*gameTime)")) != std::string_view::npos);
    CHECK(gameTimeCase.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = *payload};")) != std::string_view::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter encodes each token's payload "
          "using the shared little-endian codec, at the wire-documented size",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("EncodeFloatLittleEndian")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("EncodeUInt16LittleEndian")) != std::string::npos);
    //  Vitals is 6 float32 fields (24 bytes); the fixed size assignment pins
    //  the coherent current/maximum payload shape.
    CHECK(source.find(NormalizeWhitespace("payload.size = 24;")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter encodes and copies vitals in "
          "health, magicka, stamina order, matching the host's own decode "
          "offsets",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  CharacterCaptureHandler.cs's ApplyVitals decodes current values first, then
    //  maximum values; the source's field and copy order must match it.
    std::string source = RouterSource();

    auto healthEncodePosition = source.find("EncodeFloatLittleEndian(vitals->health)");
    auto magickaEncodePosition = source.find("EncodeFloatLittleEndian(vitals->magicka)");
    auto staminaEncodePosition = source.find("EncodeFloatLittleEndian(vitals->stamina)");
    auto healthMaxEncodePosition = source.find("EncodeFloatLittleEndian(vitals->healthMax)");
    auto magickaMaxEncodePosition = source.find("EncodeFloatLittleEndian(vitals->magickaMax)");
    auto staminaMaxEncodePosition = source.find("EncodeFloatLittleEndian(vitals->staminaMax)");
    REQUIRE(healthEncodePosition != std::string::npos);
    REQUIRE(magickaEncodePosition != std::string::npos);
    REQUIRE(staminaEncodePosition != std::string::npos);
    REQUIRE(healthMaxEncodePosition != std::string::npos);
    REQUIRE(magickaMaxEncodePosition != std::string::npos);
    REQUIRE(staminaMaxEncodePosition != std::string::npos);
    CHECK(healthEncodePosition < magickaEncodePosition);
    CHECK(magickaEncodePosition < staminaEncodePosition);
    CHECK(staminaEncodePosition < healthMaxEncodePosition);
    CHECK(healthMaxEncodePosition < magickaMaxEncodePosition);
    CHECK(magickaMaxEncodePosition < staminaMaxEncodePosition);

    auto healthCopyPosition = source.find("std::ranges::copy(health,");
    auto magickaCopyPosition = source.find("std::ranges::copy(magicka,");
    auto staminaCopyPosition = source.find("std::ranges::copy(stamina,");
    auto healthMaxCopyPosition = source.find("std::ranges::copy(healthMax,");
    auto magickaMaxCopyPosition = source.find("std::ranges::copy(magickaMax,");
    auto staminaMaxCopyPosition = source.find("std::ranges::copy(staminaMax,");
    REQUIRE(healthCopyPosition != std::string::npos);
    REQUIRE(magickaCopyPosition != std::string::npos);
    REQUIRE(staminaCopyPosition != std::string::npos);
    REQUIRE(healthMaxCopyPosition != std::string::npos);
    REQUIRE(magickaMaxCopyPosition != std::string::npos);
    REQUIRE(staminaMaxCopyPosition != std::string::npos);
    CHECK(healthCopyPosition < magickaCopyPosition);
    CHECK(magickaCopyPosition < staminaCopyPosition);
    CHECK(staminaCopyPosition < healthMaxCopyPosition);
    CHECK(healthMaxCopyPosition < magickaMaxCopyPosition);
    CHECK(magickaMaxCopyPosition < staminaMaxCopyPosition);
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
