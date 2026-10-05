#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "capture/tracked_quest_page_codec.hpp"
#include "capture/tracked_quest_page_request.hpp"
#include "capture/tracked_quest_page_scanner.hpp"

using namespace dovahlink::adapter::capture;

///  Represents one test quest candidate, including entries the engine scan skips.
struct FakeQuest {
    ///  Whether the engine enumeration contains a null quest pointer.
    bool isNull = false;
    ///  Whether the quest's engine tracking predicate is active.
    bool isTracked = false;
    ///  The quest's runtime FormID.
    std::uint32_t questId = 0;
};

///  Represents one raw objective instance and whether it belongs to the requested quest.
struct FakeObjectiveInstance {
    ///  Whether this record belongs to the requested quest.
    bool belongsToQuest = true;
    ///  Whether its authored text is valid for page capture.
    bool hasValidText = true;
    ///  The copied objective facts.
    TrackedQuestObjectiveFact fact{};
};

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

TEST_CASE("tracked quest ID scanning returns short pages without counting skipped quests") {
    const std::array<FakeQuest, 6> quests{
        FakeQuest{.isNull = true},
        FakeQuest{.isTracked = false, .questId = 10},
        FakeQuest{.isTracked = true, .questId = 1},
        FakeQuest{.isNull = true, .isTracked = true},
        FakeQuest{.isTracked = true, .questId = 2},
        FakeQuest{.isTracked = false, .questId = 11},
    };

    const auto payload = ScanTrackedQuestIdsPage(
        quests, 0,
        [](const FakeQuest& quest) { return !quest.isNull && quest.isTracked; },
        [](const FakeQuest& quest) { return quest.questId; });

    REQUIRE(payload.has_value());
    CHECK(payload->bytes[0] == std::byte{2});
    CHECK(payload->bytes[1] == std::byte{0});
    CHECK(payload->bytes[2] == std::byte{1});
    CHECK(payload->bytes[6] == std::byte{2});
}

TEST_CASE("tracked quest ID scanning handles exact and multiple pages") {
    std::vector<FakeQuest> quests;
    for (std::uint32_t id = 1; id <= 33; ++id) {
        quests.push_back(FakeQuest{.isTracked = true, .questId = id});
    }

    std::size_t predicateReads = 0;
    std::size_t idReads = 0;
    const auto firstPage = ScanTrackedQuestIdsPage(
        quests, 0,
        [&predicateReads](const FakeQuest& quest) {
            ++predicateReads;
            return quest.isTracked;
        },
        [&idReads](const FakeQuest& quest) {
            ++idReads;
            return quest.questId;
        });
    REQUIRE(firstPage.has_value());
    CHECK(firstPage->bytes[0] == std::byte{32});
    CHECK(firstPage->bytes[1] == std::byte{1});
    CHECK(firstPage->bytes[2] == std::byte{1});
    CHECK(firstPage->bytes[firstPage->size - 4] == std::byte{32});
    CHECK(predicateReads == 33);
    CHECK(idReads == 32);

    const auto secondPage = ScanTrackedQuestIdsPage(
        quests, 32, [](const FakeQuest& quest) { return quest.isTracked; },
        [](const FakeQuest& quest) { return quest.questId; });
    REQUIRE(secondPage.has_value());
    CHECK(secondPage->bytes[0] == std::byte{1});
    CHECK(secondPage->bytes[1] == std::byte{0});
    CHECK(secondPage->bytes[2] == std::byte{33});

    quests.pop_back();
    const auto exactPage = ScanTrackedQuestIdsPage(
        quests, 0, [](const FakeQuest& quest) { return quest.isTracked; },
        [](const FakeQuest& quest) { return quest.questId; });
    REQUIRE(exactPage.has_value());
    CHECK(exactPage->bytes[0] == std::byte{32});
    CHECK(exactPage->bytes[1] == std::byte{0});
}

TEST_CASE("tracked quest ID scanning pages beyond the Host complete-collection limit") {
    std::vector<FakeQuest> quests;
    for (std::uint32_t id = 1; id <= 160; ++id) {
        quests.push_back(FakeQuest{.isTracked = true, .questId = id});
    }

    const auto payload = ScanTrackedQuestIdsPage(
        quests, 0, [](const FakeQuest& quest) { return quest.isTracked; },
        [](const FakeQuest& quest) { return quest.questId; });

    REQUIRE(payload.has_value());
    CHECK(payload->bytes[0] == std::byte{32});
    CHECK(payload->bytes[1] == std::byte{1});
}

TEST_CASE("tracked quest ID scanning rejects zero runtime IDs") {
    const std::array<FakeQuest, 1> zeroId{FakeQuest{.isTracked = true}};
    CHECK_FALSE(ScanTrackedQuestIdsPage(
        zeroId, 0, [](const FakeQuest& quest) { return quest.isTracked; },
        [](const FakeQuest& quest) { return quest.questId; }));
}

TEST_CASE("objective scanning pages by raw quest-owned record offsets") {
    std::vector<FakeObjectiveInstance> instances;
    //  Alternating instance IDs model current and prior quest instances in the raw engine sequence.
    for (std::uint16_t index = 0; index < 34; ++index) {
        instances.push_back(FakeObjectiveInstance{
            .belongsToQuest = index != 1,
            .fact = TrackedQuestObjectiveFact{
                .index = index,
                .instanceId = index % 2 == 0 ? 2u : 1u,
                .state = 1,
                .text = std::nullopt,
            },
        });
    }

    const auto firstPage = ScanTrackedQuestObjectivesPage(
        1, 0, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });
    REQUIRE(firstPage.has_value());
    CHECK(firstPage->bytes[4] == std::byte{30});
    CHECK(firstPage->bytes[7] == std::byte{30});
    CHECK(firstPage->bytes[8] == std::byte{0});
    CHECK(firstPage->bytes[10] == std::byte{2});
    CHECK(firstPage->bytes[firstPage->size - 8] == std::byte{30});

    const auto secondPage = ScanTrackedQuestObjectivesPage(
        1, 30, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });
    REQUIRE(secondPage.has_value());
    CHECK(secondPage->bytes[4] == std::byte{33});
    CHECK(secondPage->bytes[7] == std::byte{3});
    CHECK(secondPage->bytes[6] == std::byte{0});
    CHECK(secondPage->bytes[8] == std::byte{31});
    CHECK(secondPage->bytes[10] == std::byte{1});
}

TEST_CASE("objective scanning uses UTF-8 byte sizes for page boundaries") {
    std::string firstUtf8;
    std::string secondUtf8;
    for (int index = 0; index < 63; ++index) {
        firstUtf8 += "é";
    }
    for (int index = 0; index < 47; ++index) {
        secondUtf8 += "é";
    }
    secondUtf8 += "x";
    const std::array<FakeObjectiveInstance, 4> instances{
        FakeObjectiveInstance{.fact = TrackedQuestObjectiveFact{
                                  .index = 1, .instanceId = 1, .state = 1, .text = std::string_view(firstUtf8)}},
        FakeObjectiveInstance{.fact = TrackedQuestObjectiveFact{.index = 2, .instanceId = 1, .state = 1, .text = std::string_view(secondUtf8)}},
        FakeObjectiveInstance{.fact = TrackedQuestObjectiveFact{.index = 3, .instanceId = 1, .state = 1, .text = std::string_view("yz")}},
        FakeObjectiveInstance{.fact = TrackedQuestObjectiveFact{.index = 4, .instanceId = 1, .state = 1, .text = std::nullopt}},
    };

    const auto firstPage = ScanTrackedQuestObjectivesPage(
        1, 0, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });

    REQUIRE(firstPage.has_value());
    CHECK(firstPage->size == 255);
    CHECK(firstPage->bytes[4] == std::byte{3});
    CHECK(firstPage->bytes[6] == std::byte{1});
    CHECK(firstPage->bytes[8] == std::byte{1});
    CHECK(firstPage->bytes[142] == std::byte{2});
    CHECK(firstPage->bytes[245] == std::byte{3});

    const auto nextPage = ScanTrackedQuestObjectivesPage(
        1, 3, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });

    REQUIRE(nextPage.has_value());
    CHECK(nextPage->bytes[4] == std::byte{4});
    CHECK(nextPage->bytes[6] == std::byte{0});
    CHECK(nextPage->bytes[7] == std::byte{1});
    CHECK(nextPage->bytes[8] == std::byte{4});
}

TEST_CASE("objective scanning rejects invalid captured text") {
    const std::array<FakeObjectiveInstance, 1> instances{
        FakeObjectiveInstance{.hasValidText = false},
    };

    CHECK_FALSE(ScanTrackedQuestObjectivesPage(
        1, 0, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        }));
}

TEST_CASE("objective scanning fails closed when another cursor cannot be represented") {
    std::vector<FakeObjectiveInstance> instances;
    for (std::uint32_t index = 0; index <= 65535; ++index) {
        instances.push_back(FakeObjectiveInstance{
            .fact = TrackedQuestObjectiveFact{.index = 1, .instanceId = 1, .state = 1}});
    }

    CHECK_FALSE(ScanTrackedQuestObjectivesPage(
        1, (std::numeric_limits<std::uint16_t>::max)(), instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        }));

    const std::vector<FakeObjectiveInstance> noInstances;
    const auto emptyPage = ScanTrackedQuestObjectivesPage(
        1, (std::numeric_limits<std::uint16_t>::max)(), noInstances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });

    REQUIRE(emptyPage.has_value());
    CHECK(emptyPage->bytes[4] == std::byte{0xFF});
    CHECK(emptyPage->bytes[5] == std::byte{0xFF});
    CHECK(emptyPage->bytes[6] == std::byte{0});
    CHECK(emptyPage->bytes[7] == std::byte{0});
}

TEST_CASE("objective scanning stays pageable after the Host aggregate limit") {
    std::vector<FakeObjectiveInstance> instances;
    for (std::uint16_t index = 0; index < 1100; ++index) {
        instances.push_back(FakeObjectiveInstance{
            .fact = TrackedQuestObjectiveFact{.index = index, .instanceId = 1, .state = 1}});
    }

    const auto page = ScanTrackedQuestObjectivesPage(
        1, 1024, instances,
        [](const FakeObjectiveInstance& instance) { return instance.belongsToQuest; },
        [](const FakeObjectiveInstance& instance) -> std::optional<TrackedQuestObjectiveFact> {
            return instance.hasValidText ? std::optional(instance.fact) : std::nullopt;
        });

    REQUIRE(page.has_value());
    CHECK(page->bytes[4] == std::byte{30});
    CHECK(page->bytes[6] == std::byte{1});
    CHECK(page->bytes[7] == std::byte{30});
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

TEST_CASE("objective page encoding preserves every raw engine state and zero instance IDs") {
    const std::array<TrackedQuestObjectiveFact, 6> objectives{
        TrackedQuestObjectiveFact{.index = 0, .instanceId = 0, .state = 0},
        TrackedQuestObjectiveFact{.index = 1, .instanceId = 1, .state = 1},
        TrackedQuestObjectiveFact{.index = 2, .instanceId = 2, .state = 2},
        TrackedQuestObjectiveFact{.index = 3, .instanceId = 3, .state = 3},
        TrackedQuestObjectiveFact{.index = 4, .instanceId = 4, .state = 4},
        TrackedQuestObjectiveFact{.index = 5, .instanceId = 5, .state = 5},
    };

    const auto payload = TrackedQuestPageCodec::EncodeObjectives(1, 6, false, objectives);

    REQUIRE(payload.has_value());
    for (std::size_t index = 0; index < objectives.size(); ++index) {
        CHECK(payload->bytes[8 + index * 8 + 6] == static_cast<std::byte>(index));
    }
    CHECK(payload->bytes[10] == std::byte{0});
    CHECK(payload->bytes[11] == std::byte{0});
    CHECK(payload->bytes[12] == std::byte{0});
    CHECK(payload->bytes[13] == std::byte{0});
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
                                          .cursor = 65504}));
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
                                          .cursor = (std::numeric_limits<std::uint16_t>::max)()}));
    CHECK_FALSE(IsValidTrackedQuestPageRequest({.kind = TrackedQuestPageKind::kObjectives,
                                                .questId = 0}));
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
