#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

#include "capture/tracked_quest_page_codec.hpp"
#include "capture/tracked_quest_page_request.hpp"

using namespace dovahlink::adapter::capture;

TEST_CASE("tracked quest ID page encoding is bounded and little-endian") {
    std::array<std::uint32_t, kTrackedQuestIdsPerPage> ids{};
    for (std::size_t index = 0; index < ids.size(); ++index) {
        ids[index] = static_cast<std::uint32_t>(index + 1);
    }
    ids.front() = 0x12345678;
    ids.back() = 0x01020304;

    auto payload = TrackedQuestPageCodec::EncodeQuestIds(ids, true);

    REQUIRE(payload.has_value());
    CHECK(payload->size == 2 + 4 * kTrackedQuestIdsPerPage);
    CHECK(payload->bytes[0] == static_cast<std::byte>(kTrackedQuestIdsPerPage));
    CHECK(payload->bytes[1] == std::byte{1});
    CHECK(payload->bytes[2] == std::byte{0x78});
    CHECK(payload->bytes[5] == std::byte{0x12});
    CHECK(payload->bytes[payload->size - 4] == std::byte{0x04});
}

TEST_CASE("tracked quest ID page selection continues by cursor and enforces the full-list bound") {
    std::array<std::uint32_t, kMaxTrackedQuests> ids{};
    for (std::size_t index = 0; index < ids.size(); ++index) {
        ids[index] = static_cast<std::uint32_t>(index + 1);
    }

    auto secondPage = TrackedQuestPageCodec::EncodeQuestIdPage(ids, 32);
    REQUIRE(secondPage.has_value());
    CHECK(secondPage->size == 2 + 4 * kTrackedQuestIdsPerPage);
    CHECK(secondPage->bytes[1] == std::byte{1});
    CHECK(secondPage->bytes[2] == std::byte{33});

    auto lastPage = TrackedQuestPageCodec::EncodeQuestIdPage(ids, 96);
    REQUIRE(lastPage.has_value());
    CHECK(lastPage->bytes[1] == std::byte{0});
    CHECK(lastPage->bytes[2] == std::byte{97});

    std::array<std::uint32_t, kMaxTrackedQuests + 1> oversized{};
    CHECK_FALSE(TrackedQuestPageCodec::EncodeQuestIdPage(oversized, 0));
}

TEST_CASE("tracked quest page encoding rejects malformed IDs and text") {
    const std::array<std::uint32_t, 1> zeroId{0};
    const std::array<std::uint32_t, 0> noIds{};
    CHECK_FALSE(TrackedQuestPageCodec::EncodeQuestIds(zeroId, false));
    CHECK_FALSE(TrackedQuestPageCodec::EncodeQuestIds(noIds, true));

    CHECK_FALSE(TrackedQuestPageCodec::EncodeMetadata(0, 8, 1, "Title"));
    CHECK_FALSE(TrackedQuestPageCodec::EncodeMetadata(1, 8, 1, ""));
    CHECK_FALSE(TrackedQuestPageCodec::EncodeMetadata(1, 8, 1, std::string(127, 'x')));
    CHECK_FALSE(TrackedQuestPageCodec::EncodeMetadata(1, 8, 1, std::string_view("\xC0\xAF", 2)));
}

TEST_CASE("quest metadata preserves raw type and localized UTF-8 title") {
    auto payload = TrackedQuestPageCodec::EncodeMetadata(0x12345678, 0xFE, 0x90ABCDEF, "Draugr — Jarl's Favor");

    REQUIRE(payload.has_value());
    CHECK(payload->size == 10 + std::string_view("Draugr — Jarl's Favor").size());
    CHECK(payload->bytes[0] == std::byte{0x78});
    CHECK(payload->bytes[3] == std::byte{0x12});
    CHECK(payload->bytes[4] == std::byte{0xFE});
    CHECK(payload->bytes[5] == std::byte{0xEF});
    CHECK(payload->bytes[8] == std::byte{0x90});
}

TEST_CASE("objective page encoding retains instance state and nullable text") {
    const std::array<TrackedQuestObjectiveFact, 2> objectives{
        TrackedQuestObjectiveFact{.index = 30, .instanceId = 9, .state = 5, .text = std::string_view("localized objective")},
        TrackedQuestObjectiveFact{.index = 31, .instanceId = 9, .state = 0, .text = std::nullopt},
    };

    auto payload = TrackedQuestPageCodec::EncodeObjectives(0x12345678, 2, false, objectives);

    REQUIRE(payload.has_value());
    CHECK(payload->size == 8 + 8 + std::string_view("localized objective").size() + 8);
    CHECK(payload->bytes[0] == std::byte{0x78});
    CHECK(payload->bytes[4] == std::byte{2});
    CHECK(payload->bytes[7] == std::byte{2});
    CHECK(payload->bytes[8] == std::byte{30});
    CHECK(payload->bytes[14] == std::byte{5});
    const std::size_t secondEntry = 8 + 8 + std::string_view("localized objective").size();
    CHECK(payload->bytes[secondEntry + 7] == std::byte{0xFF});
}

TEST_CASE("objective page encoding fails instead of truncating an oversized page") {
    std::array<TrackedQuestObjectiveFact, 31> objectives{};
    for (std::size_t index = 0; index < objectives.size(); ++index) {
        objectives[index] = TrackedQuestObjectiveFact{
            .index = static_cast<std::uint16_t>(index), .instanceId = 1, .state = 1, .text = std::nullopt};
    }
    CHECK_FALSE(TrackedQuestPageCodec::EncodeObjectives(1, 31, false, objectives));

    const std::array<TrackedQuestObjectiveFact, 1> tooLong{
        TrackedQuestObjectiveFact{.index = 1, .instanceId = 1, .state = 1, .text = std::string(127, 'x')}};
    CHECK_FALSE(TrackedQuestPageCodec::EncodeObjectives(1, 1, false, tooLong));
    CHECK_FALSE(TrackedQuestPageCodec::EncodeObjectives(1, 1, true, tooLong));
}

TEST_CASE("objective page encoding accepts the exact 255-byte payload bound") {
    const std::string firstText(126, 'a');
    const std::string secondText(105, 'b');
    const std::array<TrackedQuestObjectiveFact, 2> objectives{
        TrackedQuestObjectiveFact{.index = 1, .instanceId = 1, .state = 1, .text = std::string_view(firstText)},
        TrackedQuestObjectiveFact{.index = 2, .instanceId = 1, .state = 2, .text = std::string_view(secondText)},
    };

    auto payload = TrackedQuestPageCodec::EncodeObjectives(1, 2, false, objectives);

    REQUIRE(payload.has_value());
    CHECK(payload->size == 255);
}

TEST_CASE("tracked quest page requests enforce operation-specific bounds") {
    CHECK(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kTrackedQuestIds,
                                          .cursor = 96}));
    CHECK_FALSE(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kTrackedQuestIds,
                                                .questId = 1}));
    CHECK_FALSE(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kTrackedQuestIds,
                                                .cursor = 33}));
    CHECK(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kQuestMetadata,
                                          .questId = 1}));
    CHECK_FALSE(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kQuestMetadata,
                                                .questId = 1,
                                                .cursor = 1}));
    CHECK(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kObjectives,
                                          .questId = 1,
                                          .cursor = 1024}));
    CHECK_FALSE(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kObjectives,
                                                .questId = 0}));
}

TEST_CASE("tracked quest objective count fails closed exactly past its aggregate bound") {
    CHECK(IsTrackedQuestObjectiveCountWithinLimit(kMaxTrackedQuestObjectives));
    CHECK_FALSE(IsTrackedQuestObjectiveCountWithinLimit(kMaxTrackedQuestObjectives + 1));
}

TEST_CASE("quest text extraction handles missing, malformed, and oversized runtime strings") {
    CHECK_FALSE(TryMakeTrackedQuestTextView(nullptr, true));
    CHECK_FALSE(TryMakeTrackedQuestTextView("", false));
    CHECK(TryMakeTrackedQuestTextView("", true).has_value());

    const std::string exactLimit(kMaxTrackedQuestTextBytes, 'x');
    CHECK(TryMakeTrackedQuestTextView(exactLimit.c_str(), false).has_value());
    const std::string oversized(kMaxTrackedQuestTextBytes + 1, 'x');
    CHECK_FALSE(TryMakeTrackedQuestTextView(oversized.c_str(), false));

    std::array<char, kMaxTrackedQuestTextBytes + 1> unterminated{};
    for (char& value : unterminated) {
        value = 'x';
    }
    std::array<char, kMaxTrackedQuestTextBytes + 1> malformed{};
    malformed[0] = static_cast<char>(0xC0);
    malformed[1] = static_cast<char>(0xAF);
    malformed[2] = '\0';
    CHECK_FALSE(TryMakeTrackedQuestTextView(unterminated.data(), true));
    CHECK_FALSE(TryMakeTrackedQuestTextView(malformed.data(), true));
}
