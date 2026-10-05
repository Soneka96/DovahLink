#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>

#include "capture/captured_payload.hpp"
#include "capture/utf8.hpp"
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
    if (text->empty() || text->size() > target.size() || !IsValidUtf8(*text)) {
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
        if (length > value.size() || (length > 0 && !detail::IsValidUtf8(std::string_view(value.data(), length)))) {
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
