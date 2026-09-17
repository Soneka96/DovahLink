#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

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
              ".payload = std::move(payload)};")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace(
              ".status = dispatch::SampleCaptureStatus::kAvailable,\n"
              ".payload = std::vector<std::byte>(encoded.begin(), encoded.end())};")) !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter encodes each token's payload "
          "using the shared little-endian codec, at the wire-documented size",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("EncodeFloatLittleEndian")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("EncodeUInt16LittleEndian")) != std::string::npos);
    //  Vitals is 3 float32 fields (12 bytes); the reservation size pins that
    //  a later edit cannot silently add or drop a field without this test
    //  failing.
    CHECK(source.find(NormalizeWhitespace("payload.reserve(12)")) != std::string::npos);
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
              ".capturedValue = std::vector<std::byte>(encoded.begin(), encoded.end()),")) !=
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
              ".playContextId = playContextState_.CurrentPlayContext(),")) !=
          std::string::npos);
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
