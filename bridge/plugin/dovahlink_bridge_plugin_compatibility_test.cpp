#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

namespace {

/// Counts non-overlapping occurrences of a substring in source text.
/// @param haystack Text to search.
/// @param needle Substring to count.
std::size_t CountOccurrences(const std::string& haystack, const std::string& needle) {
    std::size_t count = 0;
    std::size_t pos = 0;
    while ((pos = haystack.find(needle, pos)) != std::string::npos) {
        ++count;
        pos += needle.size();
    }
    return count;
}

/// Reads the plugin entry point's own source text for structural assertions.
std::string ReadPluginSource() {
    std::ifstream file(DOVAHLINK_PLUGIN_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

}  // namespace

// ApplyAlwaysActiveSetting and InstallAchievementCompatibilityPatch touch
// CommonLib runtime state directly (ai/context/skse/testing.md excludes such
// adapters from unit testing -- there is no Skyrim process to run against),
// so this file asserts the plugin entry point's wiring structurally instead:
// each call exists exactly once, gated behind its own config flag, so a
// future edit cannot accidentally call either unconditionally or drop the
// gate.
TEST_CASE("SKSEPluginLoad calls ApplyAlwaysActiveSetting exactly once, gated by its config flag",
          "[plugin][compatibility]") {
    std::string source = ReadPluginSource();
    CHECK(CountOccurrences(source, "ApplyAlwaysActiveSetting(") == 1);

    std::size_t guardPos = source.find("if (behaviorConfig.alwaysActive)");
    REQUIRE(guardPos != std::string::npos);
    std::size_t callPos = source.find("ApplyAlwaysActiveSetting(");
    REQUIRE(callPos != std::string::npos);
    CHECK(guardPos < callPos);
}

TEST_CASE("SKSEPluginLoad calls InstallAchievementCompatibilityPatch exactly once, gated by its config flag",
          "[plugin][compatibility]") {
    std::string source = ReadPluginSource();
    CHECK(CountOccurrences(source, "InstallAchievementCompatibilityPatch(") == 1);

    std::size_t guardPos = source.find("if (behaviorConfig.achievementCompat)");
    REQUIRE(guardPos != std::string::npos);
    std::size_t callPos = source.find("InstallAchievementCompatibilityPatch(");
    REQUIRE(callPos != std::string::npos);
    CHECK(guardPos < callPos);
}

TEST_CASE("SKSEPluginLoad reads the compatibility config before starting the coordinator",
          "[plugin][compatibility]") {
    std::string source = ReadPluginSource();
    std::size_t configPos = source.find("ReadGameBehaviorConfig(");
    REQUIRE(configPos != std::string::npos);
    std::size_t startPos = source.find("coordinator.Start()");
    REQUIRE(startPos != std::string::npos);
    CHECK(configPos < startPos);
}

// The three tests above each check "guard before its own call" independently,
// which alone would not catch the two if-blocks being swapped (each call
// would still individually sit after some guard). This asserts the stricter
// top-to-bottom order of all four positions, which only holds if each call
// falls inside its own guard's block rather than the other one's.
TEST_CASE("SKSEPluginLoad's two compatibility guards and calls are not swapped",
          "[plugin][compatibility]") {
    std::string source = ReadPluginSource();
    std::size_t alwaysActiveGuard = source.find("if (behaviorConfig.alwaysActive)");
    std::size_t alwaysActiveCall = source.find("ApplyAlwaysActiveSetting(");
    std::size_t achievementGuard = source.find("if (behaviorConfig.achievementCompat)");
    std::size_t achievementCall = source.find("InstallAchievementCompatibilityPatch(");
    REQUIRE(alwaysActiveGuard != std::string::npos);
    REQUIRE(alwaysActiveCall != std::string::npos);
    REQUIRE(achievementGuard != std::string::npos);
    REQUIRE(achievementCall != std::string::npos);

    CHECK(alwaysActiveGuard < alwaysActiveCall);
    CHECK(alwaysActiveCall < achievementGuard);
    CHECK(achievementGuard < achievementCall);
}

TEST_CASE("SKSEPluginLoad logs both compatibility flags unconditionally", "[plugin][compatibility]") {
    std::string source = ReadPluginSource();
    std::size_t configPos = source.find("ReadGameBehaviorConfig(");
    std::size_t alwaysActiveLog = source.find("Always-active mode: {}");
    std::size_t achievementLog = source.find("Achievement compatibility: {}");
    std::size_t alwaysActiveGuard = source.find("if (behaviorConfig.alwaysActive)");
    REQUIRE(configPos != std::string::npos);
    REQUIRE(alwaysActiveLog != std::string::npos);
    REQUIRE(achievementLog != std::string::npos);
    REQUIRE(alwaysActiveGuard != std::string::npos);

    // Both log lines must fall between reading the config and the first `if`
    // guard, proving they run unconditionally rather than having been moved
    // inside either branch.
    CHECK(configPos < alwaysActiveLog);
    CHECK(alwaysActiveLog < achievementLog);
    CHECK(achievementLog < alwaysActiveGuard);
}
