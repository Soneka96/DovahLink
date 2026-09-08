#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <utility>

using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Counts non-overlapping occurrences of `needle` in `haystack`.
std::size_t CountOccurrences(const std::string &haystack,
                             const std::string &needle) {
  std::size_t count = 0;
  std::size_t position = 0;
  while ((position = haystack.find(needle, position)) != std::string::npos) {
    ++count;
    position += needle.size();
  }
  return count;
}

///  Finds the index of the `}` matching the `{` at `openBracePos`, accounting
///  for nested braces.
std::size_t FindMatchingCloseBrace(const std::string &source,
                                   std::size_t openBracePos) {
  int depth = 0;
  for (std::size_t i = openBracePos; i < source.size(); ++i) {
    if (source[i] == '{') {
      ++depth;
    } else if (source[i] == '}') {
      --depth;
      if (depth == 0) {
        return i;
      }
    }
  }
  return std::string::npos;
}

///  Finds the `{`/`}` index pair of the block immediately following an `if
///  (...)` guard, by locating the next `{` after `guardPos` and that brace's
///  match.
std::pair<std::size_t, std::size_t>
FindGuardBlockRange(const std::string &source, std::size_t guardPos) {
  std::size_t openBrace = source.find('{', guardPos);
  if (openBrace == std::string::npos) {
    return {std::string::npos, std::string::npos};
  }
  return {openBrace, FindMatchingCloseBrace(source, openBrace)};
}

} //  namespace

TEST_CASE("FindMatchingCloseBrace matches through nested braces",
          "[plugin][compatibility]") {
  //  The real plugin source these helpers scan never nests braces this
  //  deeply (each guard's body is one statement), so this proves the
  //  depth-counting itself independent of that coincidence.
  std::string source =
      "before { outer { middle { inner } middle } outer } after";
  std::size_t openBrace = source.find('{');
  REQUIRE(openBrace != std::string::npos);

  std::size_t closeBrace = FindMatchingCloseBrace(source, openBrace);

  REQUIRE(closeBrace != std::string::npos);
  //  Matches the outermost brace, not the first "}" encountered (which
  //  would be "inner"'s).
  CHECK(source.substr(closeBrace) == "} after");
}

TEST_CASE(
    "FindMatchingCloseBrace reports no match for an unbalanced open brace",
    "[plugin][compatibility]") {
  std::string source = "before { outer { inner } after";
  std::size_t openBrace = source.find('{');
  REQUIRE(openBrace != std::string::npos);

  CHECK(FindMatchingCloseBrace(source, openBrace) == std::string::npos);
}

//  ApplyAlwaysActiveSetting and InstallAchievementCompatibilityPatch touch
//  CommonLib runtime state directly (ai/context/skse/testing.md excludes such
//  adapters from unit testing -- there is no Skyrim process to run against),
//  so this file asserts the plugin entry point's wiring structurally instead,
//  mirroring bridge/plugin/dovahlink_bridge_plugin_compatibility_test.cpp:
//  each call exists exactly once, gated behind its own config flag, so a
//  future edit cannot accidentally call either unconditionally or drop the
//  gate.
TEST_CASE("the adapter plugin calls ApplyAlwaysActiveSetting exactly once, "
          "gated by its config flag",
          "[plugin][compatibility]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
  CHECK(CountOccurrences(source, "ApplyAlwaysActiveSetting(") == 1);

  std::size_t guardPos = source.find("if (behaviorConfig.alwaysActive)");
  REQUIRE(guardPos != std::string::npos);
  std::size_t callPos = source.find("ApplyAlwaysActiveSetting(");
  REQUIRE(callPos != std::string::npos);

  auto [openBrace, closeBrace] = FindGuardBlockRange(source, guardPos);
  REQUIRE(closeBrace != std::string::npos);
  CHECK(callPos > openBrace);
  CHECK(callPos < closeBrace);
}

TEST_CASE("the adapter plugin calls InstallAchievementCompatibilityPatch "
          "exactly once, gated by its config flag",
          "[plugin][compatibility]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
  CHECK(CountOccurrences(source, "InstallAchievementCompatibilityPatch(") == 1);

  std::size_t guardPos = source.find("if (behaviorConfig.achievementCompat)");
  REQUIRE(guardPos != std::string::npos);
  std::size_t callPos = source.find("InstallAchievementCompatibilityPatch(");
  REQUIRE(callPos != std::string::npos);

  auto [openBrace, closeBrace] = FindGuardBlockRange(source, guardPos);
  REQUIRE(closeBrace != std::string::npos);
  CHECK(callPos > openBrace);
  CHECK(callPos < closeBrace);
}

TEST_CASE("the adapter plugin reads the compatibility config after logging "
          "is configured and before any worker-owning object is constructed",
          "[plugin][compatibility]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t setupLogging = source.find("SetupLogging();");
  std::size_t configPos = source.find("ReadAdapterGameBehaviorConfig(");
  std::size_t workerConstruction = source.find(
      "new dovahlink::adapter::capture::AdapterCaptureHandoffQueue");

  REQUIRE(setupLogging != std::string::npos);
  REQUIRE(configPos != std::string::npos);
  REQUIRE(workerConstruction != std::string::npos);
  CHECK(setupLogging < configPos);
  CHECK(configPos < workerConstruction);
}

//  The two tests above each check "call is inside its own guard's braces"
//  independently, which alone would not catch the two if-blocks being
//  swapped in position. This asserts the two blocks themselves are not
//  swapped or interleaved in the wrong order.
TEST_CASE("the adapter plugin's two compatibility guards and calls are not "
          "swapped",
          "[plugin][compatibility]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
  std::size_t alwaysActiveGuard =
      source.find("if (behaviorConfig.alwaysActive)");
  std::size_t alwaysActiveCall = source.find("ApplyAlwaysActiveSetting(");
  std::size_t achievementGuard =
      source.find("if (behaviorConfig.achievementCompat)");
  std::size_t achievementCall =
      source.find("InstallAchievementCompatibilityPatch(");
  REQUIRE(alwaysActiveGuard != std::string::npos);
  REQUIRE(alwaysActiveCall != std::string::npos);
  REQUIRE(achievementGuard != std::string::npos);
  REQUIRE(achievementCall != std::string::npos);

  auto [alwaysActiveOpen, alwaysActiveClose] =
      FindGuardBlockRange(source, alwaysActiveGuard);
  auto [achievementOpen, achievementClose] =
      FindGuardBlockRange(source, achievementGuard);
  REQUIRE(alwaysActiveClose != std::string::npos);
  REQUIRE(achievementClose != std::string::npos);

  CHECK(alwaysActiveCall > alwaysActiveOpen);
  CHECK(alwaysActiveCall < alwaysActiveClose);
  CHECK(achievementCall > achievementOpen);
  CHECK(achievementCall < achievementClose);
  CHECK(alwaysActiveClose < achievementOpen);
}

TEST_CASE("the adapter plugin rejects an unsupported runtime before "
          "installing either compatibility patch or constructing any "
          "worker-owning object",
          "[plugin][compatibility]") {
  //  InstallAchievementCompatibilityPatch resolves an Address-Library ID
  //  that can fail process startup on an unrecognized runtime (see
  //  adapter/runtime/commonlib_adapter_game_behavior_compatibility.hpp); the
  //  supported-runtime guard must reject that runtime first so this
  //  version-sensitive work, and the always-active patch, are never reached.
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t windowsCheck = source.find("if (!dovahlink::adapter::runtime::"
                                         "IsCurrentWindowsVersionSupported())");
  std::size_t windowsReturnFalse = source.find("return false;", windowsCheck);
  std::size_t runtimeVersionCheck =
      source.find("if (!dovahlink::adapter::runtime::"
                  "IsSupportedSkyrimVersion(skyrimVersion) ||");
  std::size_t skseVersionCheck = source.find(
      "!dovahlink::adapter::runtime::IsSupportedSkseVersion(skseVersion))",
      runtimeVersionCheck);
  std::size_t runtimeVersionReturnFalse =
      source.find("return false;", runtimeVersionCheck);
  std::size_t alwaysActiveCall = source.find("ApplyAlwaysActiveSetting(");
  std::size_t achievementCall =
      source.find("InstallAchievementCompatibilityPatch(");
  std::size_t workerConstruction = source.find(
      "new dovahlink::adapter::capture::AdapterCaptureHandoffQueue");

  REQUIRE(windowsCheck != std::string::npos);
  REQUIRE(windowsReturnFalse != std::string::npos);
  REQUIRE(runtimeVersionCheck != std::string::npos);
  REQUIRE(skseVersionCheck != std::string::npos);
  REQUIRE(runtimeVersionReturnFalse != std::string::npos);
  REQUIRE(alwaysActiveCall != std::string::npos);
  REQUIRE(achievementCall != std::string::npos);
  REQUIRE(workerConstruction != std::string::npos);
  CHECK(windowsCheck < windowsReturnFalse);
  CHECK(windowsReturnFalse < runtimeVersionCheck);
  //  Both the Skyrim and SKSE version checks belong to the same OR
  //  condition, so either one being unsupported independently rejects the
  //  load -- proves the SKSE check was not dropped or moved out of that
  //  condition.
  CHECK(runtimeVersionCheck < skseVersionCheck);
  CHECK(skseVersionCheck < runtimeVersionReturnFalse);
  CHECK(runtimeVersionReturnFalse < alwaysActiveCall);
  CHECK(runtimeVersionReturnFalse < achievementCall);
  CHECK(runtimeVersionReturnFalse < workerConstruction);

  //  Pins the REL::Version -> RuntimeVersion field order: a swapped index
  //  would silently compare the wrong component (for example build against
  //  minor) without any test-visible failure other than this text order.
  //  Tolerant of exact whitespace/line-wrapping so a formatter pass cannot
  //  spuriously break it.
  std::size_t skyrimVersionInit = source.find("skyrimVersion{");
  REQUIRE(skyrimVersionInit != std::string::npos);
  std::size_t skyrimIndex0 =
      source.find("skyrimVersionRel[0]", skyrimVersionInit);
  std::size_t skyrimIndex1 =
      source.find("skyrimVersionRel[1]", skyrimVersionInit);
  std::size_t skyrimIndex2 =
      source.find("skyrimVersionRel[2]", skyrimVersionInit);
  std::size_t skyrimIndex3 =
      source.find("skyrimVersionRel[3]", skyrimVersionInit);
  REQUIRE(skyrimIndex0 != std::string::npos);
  REQUIRE(skyrimIndex1 != std::string::npos);
  REQUIRE(skyrimIndex2 != std::string::npos);
  REQUIRE(skyrimIndex3 != std::string::npos);
  CHECK(skyrimIndex0 < skyrimIndex1);
  CHECK(skyrimIndex1 < skyrimIndex2);
  CHECK(skyrimIndex2 < skyrimIndex3);

  std::size_t skseVersionInit = source.find("skseVersion{");
  REQUIRE(skseVersionInit != std::string::npos);
  std::size_t skseIndex0 = source.find("skseVersionRel[0]", skseVersionInit);
  std::size_t skseIndex1 = source.find("skseVersionRel[1]", skseVersionInit);
  std::size_t skseIndex2 = source.find("skseVersionRel[2]", skseVersionInit);
  std::size_t skseIndex3 = source.find("skseVersionRel[3]", skseVersionInit);
  REQUIRE(skseIndex0 != std::string::npos);
  REQUIRE(skseIndex1 != std::string::npos);
  REQUIRE(skseIndex2 != std::string::npos);
  REQUIRE(skseIndex3 != std::string::npos);
  CHECK(skseIndex0 < skseIndex1);
  CHECK(skseIndex1 < skseIndex2);
  CHECK(skseIndex2 < skseIndex3);
}

TEST_CASE("the adapter plugin logs both compatibility flags unconditionally",
          "[plugin][compatibility]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
  std::size_t configPos = source.find("ReadAdapterGameBehaviorConfig(");
  std::size_t alwaysActiveLog = source.find("Always-active mode: {}");
  std::size_t achievementLog = source.find("Achievement compatibility: {}");
  std::size_t alwaysActiveGuard =
      source.find("if (behaviorConfig.alwaysActive)");
  REQUIRE(configPos != std::string::npos);
  REQUIRE(alwaysActiveLog != std::string::npos);
  REQUIRE(achievementLog != std::string::npos);
  REQUIRE(alwaysActiveGuard != std::string::npos);

  //  Both log lines must fall between reading the config and the first `if`
  //  guard, proving they run unconditionally rather than having been moved
  //  inside either branch.
  CHECK(configPos < alwaysActiveLog);
  CHECK(alwaysActiveLog < achievementLog);
  CHECK(achievementLog < alwaysActiveGuard);
}
