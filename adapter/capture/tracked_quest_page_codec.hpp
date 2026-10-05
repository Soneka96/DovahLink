#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <string_view>

#include "capture/captured_payload.hpp"
#include "capture/tracked_quest_page_request.hpp"
#include "capture/utf8.hpp"

namespace dovahlink::adapter::capture {

///  Maximum accepted UTF-8 byte length for a quest title or objective string.
inline constexpr std::size_t kMaxTrackedQuestTextBytes = 126;

///  Makes a bounded view over a runtime-owned NUL-terminated quest string.
///  @param value The runtime string pointer.
///  @param allowEmpty Whether an empty authored value can be preserved.
///  @return A borrowed valid UTF-8 view, or `nullopt` for absent or unusable text.
///  The runtime-owned source storage must outlive use of the returned view.
inline std::optional<std::string_view> TryMakeTrackedQuestTextView(
    const char* value, bool allowEmpty) {
    if (value == nullptr) {
        return std::nullopt;
    }
    for (std::size_t length = 0; length <= kMaxTrackedQuestTextBytes; ++length) {
        if (value[length] == '\0') {
            if (length == 0 && !allowEmpty) {
                return std::nullopt;
            }
            const std::string_view text(value, length);
            return detail::IsValidUtf8(text)
                       ? std::optional<std::string_view>(text)
                       : std::nullopt;
        }
    }
    return std::nullopt;
}

///  One raw objective-instance fact used by the bounded page encoder.
struct TrackedQuestObjectiveFact {
    ///  The objective's authored index in the quest.
    std::uint16_t index = 0;
    ///  The quest instance that owns this current objective record.
    std::uint32_t instanceId = 0;
    ///  The engine's raw objective-state value.
    std::uint8_t state = 0;
    ///  A borrowed localized authored display text, consumed only by the page encoder.
    ///  The caller must keep its engine-owned source alive through encoding.
    std::optional<std::string_view> text;
};

///  Encodes one tracked-quest page as a bounded private capture payload.
class TrackedQuestPageCodec final {
  public:
    ///  Encodes an already selected page of runtime quest IDs.
    ///  @param ids Runtime quest IDs in engine enumeration order.
    ///  @param hasMore Whether additional tracked quest IDs remain.
    ///  @return The encoded page, or `std::nullopt` for invalid or oversized facts.
    static std::optional<CapturedPayload> EncodeQuestIds(
        std::span<const std::uint32_t> ids, bool hasMore) {
        if (ids.size() > kTrackedQuestIdsPerPage ||
            (hasMore && ids.size() != kTrackedQuestIdsPerPage) ||
            std::ranges::any_of(ids, [](std::uint32_t id) { return id == 0; })) {
            return std::nullopt;
        }
        CapturedPayload payload;
        payload.bytes[0] = static_cast<std::byte>(ids.size());
        payload.bytes[1] = static_cast<std::byte>(hasMore ? 1 : 0);
        for (std::size_t index = 0; index < ids.size(); ++index) {
            const std::size_t offset = 2 + index * sizeof(std::uint32_t);
            WriteUInt32(&payload.bytes[offset], ids[index]);
        }
        payload.size = static_cast<std::uint8_t>(2 + ids.size() * sizeof(std::uint32_t));
        return payload;
    }

    ///  Encodes one quest's raw type and bounded localized title.
    ///  @param questId The nonzero runtime FormID.
    ///  @param type The raw Skyrim quest-type code.
    ///  @param currentInstanceId The quest's current engine instance ID.
    ///  @param title The localized quest title.
    ///  @return The encoded metadata, or `std::nullopt` for invalid or oversized facts.
    static std::optional<CapturedPayload> EncodeMetadata(
        std::uint32_t questId, std::uint8_t type,
        std::uint32_t currentInstanceId, std::string_view title) {
        if (questId == 0 || title.empty() ||
            title.size() > kMaxTrackedQuestTextBytes ||
            !detail::IsValidUtf8(title)) {
            return std::nullopt;
        }
        CapturedPayload payload;
        WriteUInt32(payload.bytes.data(), questId);
        payload.bytes[4] = static_cast<std::byte>(type);
        WriteUInt32(payload.bytes.data() + 5, currentInstanceId);
        payload.bytes[9] = static_cast<std::byte>(title.size());
        CopyText(payload, 10, title);
        payload.size = static_cast<std::uint8_t>(10 + title.size());
        return payload;
    }

    ///  Encodes objective-instance facts into a page no larger than 255 bytes.
    ///  Each entry carries its authored index, instance ID, raw state, and nullable localized text.
    ///  @param questId The nonzero runtime quest FormID.
    ///  @param nextCursor The first objective offset after this page.
    ///  @param hasMore Whether more raw matching objective records remain.
    ///  @param objectives The complete set of facts selected for this page.
    ///  @return The encoded page, or `std::nullopt` for invalid or oversized facts.
    static std::optional<CapturedPayload> EncodeObjectives(
        std::uint32_t questId, std::uint16_t nextCursor, bool hasMore,
        std::span<const TrackedQuestObjectiveFact> objectives) {
        if (questId == 0 || (objectives.empty() && hasMore)) {
            return std::nullopt;
        }
        CapturedPayload payload;
        WriteUInt32(payload.bytes.data(), questId);
        WriteUInt16(payload.bytes.data() + 4, nextCursor);
        payload.bytes[6] = static_cast<std::byte>(hasMore ? 1 : 0);
        payload.bytes[7] = std::byte{0};
        std::size_t position = 8;
        std::size_t count = 0;
        for (const TrackedQuestObjectiveFact& objective : objectives) {
            if (objective.text &&
                (objective.text->size() > kMaxTrackedQuestTextBytes ||
                 !detail::IsValidUtf8(*objective.text))) {
                return std::nullopt;
            }
            const std::size_t textLength = objective.text ? objective.text->size() : 0;
            const std::size_t entryBytes = 8 + textLength;
            if (position + entryBytes > payload.bytes.size() || count == 255) {
                return std::nullopt;
            }
            WriteUInt16(payload.bytes.data() + position, objective.index);
            WriteUInt32(payload.bytes.data() + position + 2, objective.instanceId);
            payload.bytes[position + 6] = static_cast<std::byte>(objective.state);
            payload.bytes[position + 7] = objective.text
                                              ? static_cast<std::byte>(textLength)
                                              : std::byte{0xFF};
            if (objective.text) {
                CopyText(payload, position + 8, *objective.text);
            }
            position += 8 + textLength;
            ++count;
        }
        payload.bytes[7] = static_cast<std::byte>(count);
        payload.size = static_cast<std::uint8_t>(position);
        return payload;
    }

  private:
    ///  Writes a little-endian 16-bit value into the fixed payload.
    static void WriteUInt16(std::byte* destination, std::uint16_t value) {
        destination[0] = static_cast<std::byte>(value & 0xFFu);
        destination[1] = static_cast<std::byte>((value >> 8) & 0xFFu);
    }

    ///  Writes a little-endian 32-bit value into the fixed payload.
    static void WriteUInt32(std::byte* destination, std::uint32_t value) {
        for (std::size_t index = 0; index < sizeof(value); ++index) {
            destination[index] = static_cast<std::byte>((value >> (index * 8)) & 0xFFu);
        }
    }

    ///  Copies validated UTF-8 into its bounded payload range.
    static void CopyText(CapturedPayload& payload, std::size_t offset, std::string_view text) {
        std::ranges::copy(text, reinterpret_cast<char*>(payload.bytes.data() + offset));
    }
};

} //  namespace dovahlink::adapter::capture
