#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Reads the CommonLib world-context diagnostic implementation for structural checks.
///  @return The diagnostic source text.
std::string WorldContextDiagnosticsSource() {
    return ReadSource(DOVAHLINK_ADAPTER_WORLD_CONTEXT_DIAGNOSTICS_SOURCE_FILE);
}

///  Reads the CommonLib capture leaf that invokes world-context diagnostics.
///  @return The native capture-router source text.
std::string NativeCaptureRouterSource() {
    return ReadSource(DOVAHLINK_ADAPTER_NATIVE_CAPTURE_ROUTER_SOURCE_FILE);
}

///  Checks a source-text invariant without depending on formatting whitespace.
///  @param source The source text to search.
///  @param needle The expression or invariant to find.
///  @return Whether the whitespace-normalized needle occurs in the source.
bool Contains(const std::string& source, std::string_view needle) {
    return NormalizeWhitespace(source).find(NormalizeWhitespace(needle)) !=
           std::string::npos;
}

} //  namespace

TEST_CASE("World-context diagnostics run only from the debug game-thread capture leaf",
          "[runtime][world_context][structural]") {
    const std::string router = NativeCaptureRouterSource();
    const std::string normalizedRouter = NormalizeWhitespace(router);
    const auto diagnostic = normalizedRouter.find("CaptureWorldContextDiagnostics();");
    const auto switchStart = normalizedRouter.find(
        NormalizeWhitespace("switch (static_cast<capture::CharacterSampleToken>"));
    REQUIRE(diagnostic != std::string::npos);
    REQUIRE(switchStart != std::string::npos);
    CHECK(Contains(router,
                   "#if !defined(NDEBUG)\nCaptureWorldContextDiagnostics();"));
    CHECK(diagnostic < switchStart);
}

TEST_CASE("Location diagnostics compare both candidates and bound parent cycles",
          "[runtime][world_context][structural]") {
    const std::string source = WorldContextDiagnosticsSource();

    CHECK(Contains(source, "player->GetPlayerRuntimeData().currentLocation"));
    CHECK(Contains(source, "cell->GetLocation()"));
    CHECK(Contains(source, "cell->GetFullName()"));
    CHECK(Contains(source, "cell->GetRuntimeData().worldSpace"));
    CHECK(Contains(source, "kMaxLocationParentDepth = 8"));
    CHECK(Contains(source,
                   "seenPointers[index] == current || seenFormIds[index] == formId"));
    CHECK(Contains(source, "truncated after "));
    CHECK(Contains(source, "cycle:"));
}

TEST_CASE("Quest diagnostics lock and bound mutable runtime collections",
          "[runtime][world_context][structural]") {
    const std::string source = WorldContextDiagnosticsSource();

    CHECK(Contains(source, "RE::BSSpinLockGuard lock(runtimeData.questTargetsLock);"));
    CHECK(Contains(source, "runtimeData.questTargets.size()"));
    CHECK(Contains(source, "runtimeData.questLog"));
    CHECK(Contains(source, "questLogCount >= kMaxQuestEntries"));
    CHECK(Contains(source, "questLogEntriesObserved="));
    CHECK(!Contains(source, "runtimeData.questLog.size()"));
    CHECK(Contains(source, "runtimeData.objectives"));
    CHECK(Contains(source, "kMaxQuestEntries = 24"));
    CHECK(Contains(source, "kMaxObjectiveEntries = 48"));
    CHECK(Contains(source, "quest->GetFormEditorID()"));
    CHECK(Contains(source, "quest->GetFullName()"));
    CHECK(Contains(source, "objective->displayText.c_str()"));
    CHECK(Contains(source, "QUEST_OBJECTIVE_STATE::kDormant"));
    CHECK(Contains(source, "focusedQuestField=not identified"));

    const auto objectiveLoop = NormalizeWhitespace(source);
    const auto objectiveLimit = objectiveLoop.find("objectiveCount>=kMaxObjectiveEntries");
    const auto objectiveIncrement = objectiveLoop.find("++objectiveCount;", objectiveLimit);
    const auto nullGuard = objectiveLoop.find("if(objective==nullptr)", objectiveIncrement);
    REQUIRE(objectiveLimit != std::string::npos);
    REQUIRE(objectiveIncrement != std::string::npos);
    REQUIRE(nullGuard != std::string::npos);
    CHECK(objectiveIncrement < nullGuard);
}

TEST_CASE("Calendar diagnostics validate backing globals before using getters",
          "[runtime][world_context][structural]") {
    const std::string source = WorldContextDiagnosticsSource();
    const auto normalizedSource = NormalizeWhitespace(source);
    const auto invalidGuard = normalizedSource.find("if(!globalsValid){");
    const auto yearGetter = normalizedSource.find("calendar->GetYear()");
    REQUIRE(invalidGuard != std::string::npos);
    REQUIRE(yearGetter != std::string::npos);

    CHECK(Contains(source, "calendar->gameYear != nullptr"));
    CHECK(Contains(source, "calendar->gameMonth != nullptr"));
    CHECK(Contains(source, "calendar->gameDay != nullptr"));
    CHECK(Contains(source, "calendar->gameHour != nullptr"));
    CHECK(Contains(source, "getterValues=skipped; fallback values not trusted"));
    CHECK(invalidGuard < yearGetter);
    CHECK(Contains(source, "calendar->gameMonth->value"));
    CHECK(Contains(source, "calendar->gameHour->value"));
    CHECK(Contains(source, "calendar->gameYear->value"));
    CHECK(Contains(source, "rawYear="));
    CHECK(Contains(source, "derivedMinute="));
    CHECK(Contains(source, "calendar->timeScale->value"));
}

TEST_CASE("World-context diagnostics throttle samples and suppress duplicate blocks",
          "[runtime][world_context][structural]") {
    const std::string source = WorldContextDiagnosticsSource();

    CHECK(Contains(source,
                   "SKSE::log::info(\"[DovahLink][WorldContext][{}]\\n{}\""));
    CHECK(!Contains(source,
                    "SKSE::log::debug(\"[DovahLink][WorldContext][{}]\\n{}\""));
    CHECK(Contains(source, "now - lastSample < std::chrono::seconds(1)"));
    CHECK(Contains(source, "if (current == previous) {"));
    CHECK(Contains(source, "catch (...) {"));
    CHECK(Contains(source, "[DovahLink][WorldContext][{}]"));
}
