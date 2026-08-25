#include "game_state/runtime_guard.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::game_state::IsCurrentWindowsVersionSupported;
using dovahlink::game_state::IsSupportedSkseVersion;
using dovahlink::game_state::IsSupportedSkyrimVersion;
using dovahlink::game_state::kSupportedSkseVersion;
using dovahlink::game_state::kSupportedSkyrimVersion;
using dovahlink::game_state::RuntimeVersion;

TEST_CASE("the current test host satisfies the neutral Windows runtime guard",
          "[game_state][runtime_guard]") {
  CHECK(IsCurrentWindowsVersionSupported());
}

TEST_CASE("IsSupportedSkyrimVersion accepts exactly 1.6.1170",
          "[game_state][runtime_guard]") {
  CHECK(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
  CHECK(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects the unsupported "
          "pre-Anniversary-Update runtime 1.5.97",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 5, 97, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a build number one below the "
          "supported version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1169, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a build number one above the "
          "supported version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1171, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a differing major version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{2, 6, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a differing minor version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 5, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a nonzero revision even with "
          "matching major/minor/build",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1170, 1}));
}

TEST_CASE("IsSupportedSkseVersion accepts exactly 2.2.6",
          "[game_state][runtime_guard]") {
  CHECK(IsSupportedSkseVersion(kSupportedSkseVersion));
  CHECK(IsSupportedSkseVersion(RuntimeVersion{2, 2, 6, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects an older SKSE version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 2, 3, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects a newer SKSE version",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 3, 0, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects a nonzero revision even with "
          "matching major/minor/build",
          "[game_state][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 2, 6, 1}));
}

TEST_CASE("IsSupportedSkyrimVersion and IsSupportedSkseVersion are independent "
          "checks",
          "[game_state][runtime_guard]") {
  // A supported Skyrim build paired with an unsupported SKSE version (or
  // vice versa) must not be conflated into a single pass/fail signal here;
  // the plugin entry point (a later step) checks and reports both.
  CHECK(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 0, 20, 0}));
}
