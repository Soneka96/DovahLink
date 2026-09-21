#include "capture/live_state_sample_codec.hpp"
#include "enums.hpp"

#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <bit>
#include <charconv>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include <string>
#include <string_view>
#include <vector>

using dovahlink::adapter::capture::CapturedPayload;
using dovahlink::adapter::capture::CharacterEventKey;
using dovahlink::adapter::capture::CharacterSampleToken;
using dovahlink::adapter::capture::EncodeFloatLittleEndian;
using dovahlink::adapter::capture::EncodeUInt16LittleEndian;
using dovahlink::adapter::capture::kMaxCapturedPayloadBytes;
using dovahlink::adapter::capture::MakeCapturedPayload;
using dovahlink::adapter::capture::TryMakeCapturedPayload;
using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("EncodeFloatLittleEndian matches the host's little-endian float decode",
          "[capture][live_state_sample_codec]") {
    //  1.5f's IEEE-754 bit pattern is 0x3FC00000; little-endian byte order
    //  places the least-significant byte first.
    std::array<std::byte, 4> encoded = EncodeFloatLittleEndian(1.5f);

    CHECK(encoded == std::array<std::byte, 4>{
                         std::byte{0x00}, std::byte{0x00}, std::byte{0xC0}, std::byte{0x3F}});
}

TEST_CASE("EncodeFloatLittleEndian round-trips zero, a negative value, and a large value",
          "[capture][live_state_sample_codec]") {
    CHECK(EncodeFloatLittleEndian(0.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x00}, std::byte{0x00}, std::byte{0x00}});
    //  -1.0f's bit pattern is 0xBF800000.
    CHECK(EncodeFloatLittleEndian(-1.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x00}, std::byte{0x80}, std::byte{0xBF}});
    //  100000.0f's bit pattern is 0x47C35000.
    CHECK(EncodeFloatLittleEndian(100000.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x50}, std::byte{0xC3}, std::byte{0x47}});
}

TEST_CASE("EncodeUInt16LittleEndian places the least-significant byte first",
          "[capture][live_state_sample_codec]") {
    CHECK(EncodeUInt16LittleEndian(0x1234) ==
          std::array<std::byte, 2>{std::byte{0x34}, std::byte{0x12}});
    CHECK(EncodeUInt16LittleEndian(0) == std::array<std::byte, 2>{std::byte{0x00}, std::byte{0x00}});
    CHECK(EncodeUInt16LittleEndian(0xFFFF) ==
          std::array<std::byte, 2>{std::byte{0xFF}, std::byte{0xFF}});
}

///  Reassembles an `EncodeFloatLittleEndian` result back into the bit
///  pattern it encoded, for round-trip comparison. Test-only: production
///  code never needs to decode on the adapter side, since the host owns
///  decoding.
std::uint32_t DecodeBitsLittleEndian(const std::array<std::byte, 4>& bytes) {
    std::uint32_t bits = 0;
    for (int index = 3; index >= 0; --index) {
        bits = (bits << 8) | std::to_integer<std::uint32_t>(bytes[static_cast<std::size_t>(index)]);
    }
    return bits;
}

TEST_CASE("EncodeFloatLittleEndian round-trips every bit pattern exactly, "
          "including NaN and Infinity",
          "[capture][live_state_sample_codec]") {
    //  Compared as bit patterns, not float equality: NaN != NaN under IEEE-754,
    //  so a float-equality round-trip check would falsely fail for NaN even
    //  when the bytes are correct.
    for (float value : {0.0f, -1.0f, 1.5f, 100000.0f, 1e-30f,
                        std::numeric_limits<float>::infinity(),
                        -std::numeric_limits<float>::infinity(),
                        std::numeric_limits<float>::quiet_NaN()}) {
        std::uint32_t originalBits = std::bit_cast<std::uint32_t>(value);
        std::uint32_t roundTrippedBits = DecodeBitsLittleEndian(EncodeFloatLittleEndian(value));
        CHECK(roundTrippedBits == originalBits);
    }
}

TEST_CASE("MakeCapturedPayload copies the source bytes and records their "
          "count as size",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, 2> source{std::byte{0x34}, std::byte{0x12}};

    CapturedPayload payload = MakeCapturedPayload(source);

    REQUIRE(payload.size == 2);
    CHECK(payload.AsSpan().size() == 2);
    CHECK(payload.AsSpan()[0] == std::byte{0x34});
    CHECK(payload.AsSpan()[1] == std::byte{0x12});
}

TEST_CASE("MakeCapturedPayload leaves every byte beyond size zeroed",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, 1> source{std::byte{0xFF}};

    CapturedPayload payload = MakeCapturedPayload(source);

    for (std::size_t index = 1; index < payload.bytes.size(); ++index) {
        CHECK(payload.bytes[index] == std::byte{0x00});
    }
}

TEST_CASE("MakeCapturedPayload fills the full buffer at the maximum size",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, kMaxCapturedPayloadBytes> source{};
    for (std::size_t index = 0; index < source.size(); ++index) {
        source[index] = static_cast<std::byte>(index);
    }

    CapturedPayload payload = MakeCapturedPayload(source);

    REQUIRE(payload.size == kMaxCapturedPayloadBytes);
    CHECK(std::ranges::equal(payload.AsSpan(), source));
}

TEST_CASE("CapturedPayload equality compares both the buffer and size, "
          "treating differently-sized empty payloads as equal since their "
          "unused bytes are always zero",
          "[capture][live_state_sample_codec]") {
    CapturedPayload empty{};
    CapturedPayload alsoEmpty = MakeCapturedPayload(std::array<std::byte, 0>{});
    std::array<std::byte, 1> oneByte{std::byte{0x01}};

    CHECK(empty == alsoEmpty);
    CHECK_FALSE(empty == MakeCapturedPayload(oneByte));
}

TEST_CASE("TryMakeCapturedPayload accepts runtime spans at and under the "
          "maximum capacity, preserving exact bytes and size",
          "[capture][live_state_sample_codec]") {
    std::vector<std::byte> empty;
    std::vector<std::byte> twoBytes{std::byte{0x01}, std::byte{0x02}};
    std::vector<std::byte> fourBytes{
        std::byte{0x01}, std::byte{0x02}, std::byte{0x03}, std::byte{0x04}};
    std::vector<std::byte> twelveBytes(kMaxCapturedPayloadBytes);
    for (std::size_t index = 0; index < twelveBytes.size(); ++index) {
        twelveBytes[index] = static_cast<std::byte>(index);
    }

    for (const std::vector<std::byte>& source :
         {empty, twoBytes, fourBytes, twelveBytes}) {
        std::optional<CapturedPayload> payload =
            TryMakeCapturedPayload(std::span(source));

        REQUIRE(payload.has_value());
        REQUIRE(payload->size == source.size());
        CHECK(std::ranges::equal(payload->AsSpan(), source));
    }
}

TEST_CASE("TryMakeCapturedPayload fails closed for a runtime span over the "
          "maximum capacity, without truncating it",
          "[capture][live_state_sample_codec]") {
    std::vector<std::byte> oversized(kMaxCapturedPayloadBytes + 1);

    CHECK_FALSE(TryMakeCapturedPayload(std::span(oversized)).has_value());
}

//  TODO(stage4-file-extraction): Move this live-state-catalog-fixture test
//  back to its own tests/capture/live_state_catalog_fixture_test.cpp in the
//  post-Stage-4 structural cleanup PR. Temporarily colocated with the
//  sibling capture-codec test to hold this PR's changed-file count down;
//  extraction only, no behavior change.
namespace {

///  Reads one integer field from the checked-in host/adapter live-state
///  capture catalog fixture, the same way
///  `adapter_ipc_connection_test.cpp`'s `ReadPrivateIpcLimit` reads the
///  private-IPC rate-limit fixture.
std::uint32_t ReadLiveStateToken(std::string_view key) {
    const std::string source = ReadSource(DOVAHLINK_LIVE_STATE_CATALOG_FIXTURE);
    const std::string marker = "\"" + std::string(key) + "\"";
    const std::size_t keyPosition = source.find(marker);
    REQUIRE(keyPosition != std::string::npos);
    const std::size_t colon = source.find(':', keyPosition + marker.size());
    REQUIRE(colon != std::string::npos);
    const std::size_t valuePosition =
        source.find_first_not_of(" \t\r\n", colon + 1);
    REQUIRE(valuePosition != std::string::npos);

    std::uint32_t value = 0;
    const auto [end, error] = std::from_chars(
        source.data() + valuePosition, source.data() + source.size(), value);
    REQUIRE(error == std::errc{});
    REQUIRE(end != source.data() + valuePosition);
    return value;
}

} //  namespace

TEST_CASE("Adapter live-state capture enums match the shared catalog fixture",
          "[capture][live-state]") {
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals) ==
          ReadLiveStateToken("characterVitals"));
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp) ==
          ReadLiveStateToken("characterXp"));
    CHECK(static_cast<std::uint32_t>(
              CharacterSampleToken::kCharacterLevelBaseline) ==
          ReadLiveStateToken("characterLevelBaseline"));
    CHECK(static_cast<std::uint32_t>(
              CharacterEventKey::kCharacterLevelChanged) ==
          ReadLiveStateToken("characterLevelChanged"));
}
