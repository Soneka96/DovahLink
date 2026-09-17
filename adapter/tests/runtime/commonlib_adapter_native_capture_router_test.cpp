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

TEST_CASE("CommonLibAdapterNativeCaptureRouter fails closed for an unknown "
          "sample token",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("default:\nreturn std::nullopt;")) != std::string::npos);
}

TEST_CASE("CommonLibAdapterNativeCaptureRouter never returns a fabricated "
          "default when the underlying capture is unavailable",
          "[runtime][commonlib_adapter_native_capture_router][structural]") {
    //  Every arm must return std::nullopt on its own capture's failure,
    //  never fall through to an empty/zeroed payload construction.
    std::string source = NormalizeWhitespace(RouterSource());

    CHECK(source.find(NormalizeWhitespace("if (!vitals) {\nreturn std::nullopt;")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("if (!xp) {\nreturn std::nullopt;")) != std::string::npos);
    CHECK(source.find(NormalizeWhitespace("if (!level) {\nreturn std::nullopt;")) != std::string::npos);
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
