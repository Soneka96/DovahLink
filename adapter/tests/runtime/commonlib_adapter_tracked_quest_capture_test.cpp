#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("CommonLib tracked quest capture uses tracked engine facts and bounded authored text",
          "[runtime][tracked_quests][structural]") {
    const std::string source = ReadSource(DOVAHLINK_ADAPTER_TRACKED_QUEST_CAPTURE_SOURCE_FILE);

    CHECK(source.find("quest->IsActive()") != std::string::npos);
    CHECK(source.find("currentInstanceID") != std::string::npos);
    CHECK(source.find("ownerQuest") != std::string::npos);
    CHECK(source.find("objective->displayText") != std::string::npos);
    CHECK(source.find("instance.InstanceState") != std::string::npos);
    CHECK(source.find("instance.instanceID") != std::string::npos);
    CHECK(source.find("quest->GetType()") != std::string::npos);
    CHECK(source.find("TryMakeTrackedQuestTextView") != std::string::npos);
    CHECK(source.find("instance.instanceID != quest->currentInstanceID") == std::string::npos);
    CHECK(source.find("GetJournalTextForInstance") == std::string::npos);
}

TEST_CASE("CommonLib tracked quest capture fails unavailable instead of fabricating incomplete pages",
          "[runtime][tracked_quests][structural]") {
    const std::string source = ReadSource(DOVAHLINK_ADAPTER_TRACKED_QUEST_CAPTURE_SOURCE_FILE);

    CHECK(source.find("kMaxTrackedQuests") != std::string::npos);
    CHECK(source.find("IsTrackedQuestObjectiveCountWithinLimit") != std::string::npos);
    CHECK(source.find("UnavailablePage()") != std::string::npos);
    CHECK(source.find("hasMore") != std::string::npos);
}
