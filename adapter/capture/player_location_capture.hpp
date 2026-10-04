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

///  One bounded copy of the player's cell, both location sources, and worldspace.
struct PlayerLocationCapture {
    ///  The required current-cell runtime FormID.
    std::uint32_t cellFormId = 0;
    ///  Whether the current cell is interior.
    bool cellIsInterior = false;
    ///  UTF-8 bytes of the optional current-cell display name.
    std::array<char, kMaxPlayerLocationNameBytes> cellName{};
    ///  Number of meaningful bytes in `cellName`.
    std::uint8_t cellNameLength = 0;
    ///  The optional `PlayerCharacter::currentLocation` runtime FormID.
    std::uint32_t playerLocationFormId = 0;
    ///  UTF-8 bytes of the optional direct player-location display name.
    std::array<char, kMaxPlayerLocationNameBytes> playerLocationName{};
    ///  Number of meaningful bytes in `playerLocationName`.
    std::uint8_t playerLocationNameLength = 0;
    ///  The optional `TESObjectCELL::GetLocation` runtime FormID.
    std::uint32_t cellLocationFormId = 0;
    ///  UTF-8 bytes of the optional cell-location display name.
    std::array<char, kMaxPlayerLocationNameBytes> cellLocationName{};
    ///  Number of meaningful bytes in `cellLocationName`.
    std::uint8_t cellLocationNameLength = 0;
    ///  The optional current-worldspace runtime FormID.
    std::uint32_t worldspaceFormId = 0;
    ///  UTF-8 bytes of the optional worldspace display name.
    std::array<char, kMaxPlayerLocationNameBytes> worldspaceName{};
    ///  Number of meaningful bytes in `worldspaceName`.
    std::uint8_t worldspaceNameLength = 0;

    ///  Structural equality over every copied engine fact.
    bool operator==(const PlayerLocationCapture&) const = default;
};

namespace detail {

///  Checks that a byte string contains valid UTF-8 without embedded NUL.
///  @param text The byte string to check.
///  @return `true` when every sequence encodes a Unicode scalar value.
inline bool IsValidPlayerLocationUtf8(std::string_view text) {
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

///  Copies one usable optional engine name into the capture's fixed-size buffer.
///  @param text The optional UTF-8 name.
///  @param target The owned destination buffer.
///  @param length The destination's byte length.
///  @return `true` when the name is copied or omitted as absent, oversized, or malformed.
inline bool CopyPlayerLocationName(
    std::optional<std::string_view> text,
    std::array<char, kMaxPlayerLocationNameBytes>& target,
    std::uint8_t& length) {
    if (!text.has_value()) {
        return true;
    }
    if (text->empty() || text->size() > target.size() || !IsValidPlayerLocationUtf8(*text)) {
        return true;
    }
    std::ranges::copy(*text, target.begin());
    length = static_cast<std::uint8_t>(text->size());
    return true;
}

} //  namespace detail

///  Creates an owned capture without truncating optional Skyrim names.
///  @param cellFormId The required current-cell runtime FormID.
///  @param cellIsInterior Whether the current cell is interior.
///  @param cellName The optional localized cell display name.
///  @param playerLocationFormId The optional `PlayerCharacter::currentLocation` FormID.
///  @param playerLocationName Its optional localized display name.
///  @param cellLocationFormId The optional `TESObjectCELL::GetLocation` FormID.
///  @param cellLocationName Its optional localized display name.
///  @param worldspaceFormId The optional current worldspace FormID.
///  @param worldspaceName Its optional localized display name.
///  @return The copied facts, or `std::nullopt` for a missing cell. Unusable optional names are omitted.
inline std::optional<PlayerLocationCapture> TryMakePlayerLocationCapture(
    std::uint32_t cellFormId,
    bool cellIsInterior,
    std::optional<std::string_view> cellName,
    std::uint32_t playerLocationFormId,
    std::optional<std::string_view> playerLocationName,
    std::uint32_t cellLocationFormId,
    std::optional<std::string_view> cellLocationName,
    std::uint32_t worldspaceFormId,
    std::optional<std::string_view> worldspaceName) {
    if (cellFormId == 0 ||
        (playerLocationFormId == 0 && playerLocationName.has_value()) ||
        (cellLocationFormId == 0 && cellLocationName.has_value()) ||
        (worldspaceFormId == 0 && worldspaceName.has_value())) {
        return std::nullopt;
    }

    PlayerLocationCapture capture{
        .cellFormId = cellFormId,
        .cellIsInterior = cellIsInterior,
        .playerLocationFormId = playerLocationFormId,
        .cellLocationFormId = cellLocationFormId,
        .worldspaceFormId = worldspaceFormId,
    };
    if (!detail::CopyPlayerLocationName(cellName, capture.cellName, capture.cellNameLength) ||
        !detail::CopyPlayerLocationName(playerLocationName, capture.playerLocationName, capture.playerLocationNameLength) ||
        !detail::CopyPlayerLocationName(cellLocationName, capture.cellLocationName, capture.cellLocationNameLength) ||
        !detail::CopyPlayerLocationName(worldspaceName, capture.worldspaceName, capture.worldspaceNameLength)) {
        return std::nullopt;
    }
    return capture;
}

///  Encodes four optional length-prefixed names and their engine FormIDs.
///  @param capture The complete bounded engine-fact capture.
///  @return The private payload, or `std::nullopt` if its fixed capacity is exceeded.
inline std::optional<CapturedPayload> TryEncodePlayerLocationPayload(
    const PlayerLocationCapture& capture) {
    if (capture.cellFormId == 0 ||
        (capture.playerLocationFormId == 0 && capture.playerLocationNameLength > 0) ||
        (capture.cellLocationFormId == 0 && capture.cellLocationNameLength > 0) ||
        (capture.worldspaceFormId == 0 && capture.worldspaceNameLength > 0)) {
        return std::nullopt;
    }

    CapturedPayload payload;
    std::size_t offset = 0;
    const auto appendUInt32 = [&payload, &offset](std::uint32_t value) {
        for (std::size_t index = 0; index < sizeof(value); ++index) {
            payload.bytes[offset++] = static_cast<std::byte>((value >> (index * 8)) & 0xFF);
        }
    };
    const auto appendName = [&payload, &offset](const auto& value, std::uint8_t length) {
        if (length > value.size() || (length > 0 && !detail::IsValidPlayerLocationUtf8(std::string_view(value.data(), length)))) {
            return false;
        }
        payload.bytes[offset++] = static_cast<std::byte>(length);
        for (std::size_t index = 0; index < length; ++index) {
            payload.bytes[offset++] = static_cast<std::byte>(static_cast<unsigned char>(value[index]));
        }
        return true;
    };

    appendUInt32(capture.cellFormId);
    payload.bytes[offset++] = capture.cellIsInterior ? std::byte{1} : std::byte{0};
    if (!appendName(capture.cellName, capture.cellNameLength)) {
        return std::nullopt;
    }
    appendUInt32(capture.playerLocationFormId);
    if (!appendName(capture.playerLocationName, capture.playerLocationNameLength)) {
        return std::nullopt;
    }
    appendUInt32(capture.cellLocationFormId);
    if (!appendName(capture.cellLocationName, capture.cellLocationNameLength)) {
        return std::nullopt;
    }
    appendUInt32(capture.worldspaceFormId);
    if (!appendName(capture.worldspaceName, capture.worldspaceNameLength)) {
        return std::nullopt;
    }
    if (offset > payload.bytes.size()) {
        return std::nullopt;
    }
    payload.size = static_cast<std::uint8_t>(offset);
    return payload;
}

} //  namespace dovahlink::adapter::capture
