#include "capture/live_state_sample_codec.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>

using dovahlink::adapter::capture::CapturedPayload;
using dovahlink::adapter::capture::EncodeFloatLittleEndian;
using dovahlink::adapter::capture::EncodeUInt16LittleEndian;
using dovahlink::adapter::capture::kMaxCapturedPayloadBytes;
using dovahlink::adapter::capture::MakeCapturedPayload;

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
    CapturedPayload alsoEmpty = MakeCapturedPayload(std::span<const std::byte>{});
    std::array<std::byte, 1> oneByte{std::byte{0x01}};

    CHECK(empty == alsoEmpty);
    CHECK_FALSE(empty == MakeCapturedPayload(oneByte));
}
