#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>

#include "capture/captured_payload.hpp"
#include "capture/live_state_sample_codec.hpp"
#include "capture/utf8.hpp"
#include "constants.hpp"

namespace dovahlink::adapter::capture {

///  One copied set of authoritative Skyrim calendar globals and its localized month name.
struct GameTimeCapture {
    ///  Raw Skyrim game-year global value.
    float year = 0.0f;
    ///  Raw zero-based Skyrim game-month global value.
    float month = 0.0f;
    ///  Raw Skyrim game-day global value.
    float day = 0.0f;
    ///  Raw fractional Skyrim game-hour global value.
    float hour = 0.0f;
    ///  UTF-8 bytes of the localized month name.
    std::array<char, kMaxGameMonthNameBytes> monthName{};
    ///  Number of meaningful bytes in `monthName`.
    std::uint8_t monthNameLength = 0;

    ///  Structural equality over every copied calendar fact.
    bool operator==(const GameTimeCapture&) const = default;
};

///  Copies the raw calendar facts and localized month name into bounded storage.
///  @param year The backing Skyrim year global value.
///  @param month The backing zero-based Skyrim month global value.
///  @param day The backing Skyrim day global value.
///  @param hour The backing fractional Skyrim hour global value.
///  @param monthName The localized month name returned by the running game.
///  @return The owned capture, or `std::nullopt` for non-finite raw values or an empty, oversized, or invalid month name.
inline std::optional<GameTimeCapture> TryMakeGameTimeCapture(
    float year,
    float month,
    float day,
    float hour,
    std::string_view monthName) {
    if (!std::isfinite(year) || !std::isfinite(month) || !std::isfinite(day) ||
        !std::isfinite(hour) || monthName.empty() ||
        monthName.size() > kMaxGameMonthNameBytes ||
        !detail::IsValidUtf8(monthName)) {
        return std::nullopt;
    }

    GameTimeCapture capture{
        .year = year,
        .month = month,
        .day = day,
        .hour = hour,
    };
    std::ranges::copy(monthName, capture.monthName.begin());
    capture.monthNameLength = static_cast<std::uint8_t>(monthName.size());
    return capture;
}

///  Encodes four raw little-endian calendar floats followed by a length-prefixed UTF-8 month name.
///  @param capture The complete copied calendar values.
///  @return The bounded private payload, or `std::nullopt` for invalid month-name storage.
inline std::optional<CapturedPayload> TryEncodeGameTimePayload(
    const GameTimeCapture& capture) {
    if (capture.monthNameLength == 0 ||
        capture.monthNameLength > capture.monthName.size() ||
        !detail::IsValidUtf8(
            std::string_view(capture.monthName.data(), capture.monthNameLength))) {
        return std::nullopt;
    }

    CapturedPayload payload;
    std::size_t offset = 0;
    for (float value : std::array{capture.year, capture.month, capture.day, capture.hour}) {
        const std::array<std::byte, sizeof(float)> encoded = EncodeFloatLittleEndian(value);
        std::ranges::copy(encoded, payload.bytes.begin() + static_cast<std::ptrdiff_t>(offset));
        offset += encoded.size();
    }
    payload.bytes[offset++] = static_cast<std::byte>(capture.monthNameLength);
    for (std::size_t index = 0; index < capture.monthNameLength; ++index) {
        payload.bytes[offset++] = static_cast<std::byte>(
            static_cast<unsigned char>(capture.monthName[index]));
    }
    payload.size = static_cast<std::uint8_t>(offset);
    return payload;
}

} //  namespace dovahlink::adapter::capture
