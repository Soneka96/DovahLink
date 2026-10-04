#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>

#include "capture/captured_payload.hpp"
#include "constants.hpp"

namespace dovahlink::adapter::capture {

///  One complete, owned player identity observation. Both strings are copied
///  into fixed-size storage so the value may cross the capture handoff without
///  retaining borrowed Skyrim memory or allocating on the game thread.
struct CharacterIdentityCapture {
    ///  UTF-8 bytes of the player's display name, left-aligned.
    std::array<char, kMaxCharacterIdentityStringBytes> name{};
    ///  Number of meaningful bytes in `name`.
    std::uint8_t nameLength = 0;
    ///  UTF-8 bytes of the identity race's game display name, left-aligned.
    std::array<char, kMaxCharacterIdentityStringBytes> race{};
    ///  Number of meaningful bytes in `race`.
    std::uint8_t raceLength = 0;

    ///  Structural equality over every field.
    bool operator==(const CharacterIdentityCapture&) const = default;
};

namespace detail {

///  Checks that a byte string is well-formed UTF-8, excluding embedded NUL.
///  @param text The byte string to check.
///  @return `true` when every sequence is a valid Unicode scalar encoding.
inline bool IsValidIdentityUtf8(std::string_view text) {
    std::size_t index = 0;
    while (index < text.size()) {
        const auto first = static_cast<unsigned char>(text[index]);
        if (first <= 0x7F) {
            if (first == 0) {
                return false;
            }
            ++index;
            continue;
        }

        std::size_t continuationCount = 0;
        unsigned char secondMinimum = 0x80;
        unsigned char secondMaximum = 0xBF;
        if (first >= 0xC2 && first <= 0xDF) {
            continuationCount = 1;
        } else if (first >= 0xE0 && first <= 0xEF) {
            continuationCount = 2;
            if (first == 0xE0) {
                secondMinimum = 0xA0;
            } else if (first == 0xED) {
                secondMaximum = 0x9F;
            }
        } else if (first >= 0xF0 && first <= 0xF4) {
            continuationCount = 3;
            if (first == 0xF0) {
                secondMinimum = 0x90;
            } else if (first == 0xF4) {
                secondMaximum = 0x8F;
            }
        } else {
            return false;
        }

        if (index + continuationCount >= text.size()) {
            return false;
        }
        const auto second = static_cast<unsigned char>(text[index + 1]);
        if (second < secondMinimum || second > secondMaximum) {
            return false;
        }
        for (std::size_t offset = 2; offset <= continuationCount; ++offset) {
            const auto continuation = static_cast<unsigned char>(text[index + offset]);
            if (continuation < 0x80 || continuation > 0xBF) {
                return false;
            }
        }
        index += continuationCount + 1;
    }
    return true;
}

} //  namespace detail

///  Validates and copies both identity strings into bounded owned storage.
///  @param name The player display name as UTF-8 bytes.
///  @param race The game-provided identity-race display name as UTF-8 bytes.
///  @return The complete identity or `std::nullopt` for empty, oversized, or invalid text.
inline std::optional<CharacterIdentityCapture> TryMakeCharacterIdentityCapture(
    std::string_view name, std::string_view race) {
    if (name.empty() || race.empty() ||
        name.size() > kMaxCharacterIdentityStringBytes ||
        race.size() > kMaxCharacterIdentityStringBytes ||
        !detail::IsValidIdentityUtf8(name) || !detail::IsValidIdentityUtf8(race)) {
        return std::nullopt;
    }

    CharacterIdentityCapture identity;
    std::ranges::copy(name, identity.name.begin());
    identity.nameLength = static_cast<std::uint8_t>(name.size());
    std::ranges::copy(race, identity.race.begin());
    identity.raceLength = static_cast<std::uint8_t>(race.size());
    return identity;
}

///  Encodes identity as `[nameLength:u8][name UTF-8][raceLength:u8][race UTF-8]`.
///  @param identity The complete identity observation to encode.
///  @return The bounded payload, or `std::nullopt` if its fields are malformed.
inline std::optional<CapturedPayload> TryEncodeCharacterIdentityPayload(
    const CharacterIdentityCapture& identity) {
    if (identity.nameLength == 0 || identity.raceLength == 0 ||
        identity.nameLength > kMaxCharacterIdentityStringBytes ||
        identity.raceLength > kMaxCharacterIdentityStringBytes) {
        return std::nullopt;
    }

    const std::string_view name(identity.name.data(), identity.nameLength);
    const std::string_view race(identity.race.data(), identity.raceLength);
    if (!detail::IsValidIdentityUtf8(name) || !detail::IsValidIdentityUtf8(race)) {
        return std::nullopt;
    }

    CapturedPayload payload;
    std::size_t offset = 0;
    payload.bytes[offset++] = static_cast<std::byte>(identity.nameLength);
    for (char value : name) {
        payload.bytes[offset++] = static_cast<std::byte>(
            static_cast<unsigned char>(value));
    }
    payload.bytes[offset++] = static_cast<std::byte>(identity.raceLength);
    for (char value : race) {
        payload.bytes[offset++] = static_cast<std::byte>(
            static_cast<unsigned char>(value));
    }
    payload.size = static_cast<std::uint8_t>(offset);
    return payload;
}

} //  namespace dovahlink::adapter::capture
