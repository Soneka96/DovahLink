#include "runtime/adapter_runtime_guard.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::adapter::runtime::IsCurrentWindowsVersionSupported;
using dovahlink::adapter::runtime::IsSupportedSkseVersion;
using dovahlink::adapter::runtime::IsSupportedSkyrimVersion;
using dovahlink::adapter::runtime::kSupportedSkseVersion;
using dovahlink::adapter::runtime::kSupportedSkyrimVersion;
using dovahlink::adapter::runtime::RuntimeVersion;

TEST_CASE("the current test host satisfies the neutral Windows runtime guard",
          "[runtime][runtime_guard]") {
  CHECK(IsCurrentWindowsVersionSupported());
}

TEST_CASE("IsSupportedSkyrimVersion accepts exactly 1.6.1170",
          "[runtime][runtime_guard]") {
  CHECK(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
  CHECK(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects the unsupported "
          "pre-Anniversary-Update runtime 1.5.97",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 5, 97, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a build number one below the "
          "supported version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1169, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a build number one above the "
          "supported version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1171, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a differing major version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{2, 6, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a differing minor version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 5, 1170, 0}));
}

TEST_CASE("IsSupportedSkyrimVersion rejects a nonzero revision even with "
          "matching major/minor/build",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkyrimVersion(RuntimeVersion{1, 6, 1170, 1}));
}

TEST_CASE("IsSupportedSkseVersion accepts exactly 2.2.6",
          "[runtime][runtime_guard]") {
  CHECK(IsSupportedSkseVersion(kSupportedSkseVersion));
  CHECK(IsSupportedSkseVersion(RuntimeVersion{2, 2, 6, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects an older SKSE version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 2, 3, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects a newer SKSE version",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 3, 0, 0}));
}

TEST_CASE("IsSupportedSkseVersion rejects a nonzero revision even with "
          "matching major/minor/build",
          "[runtime][runtime_guard]") {
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 2, 6, 1}));
}

TEST_CASE("IsSupportedSkyrimVersion and IsSupportedSkseVersion are "
          "independent checks",
          "[runtime][runtime_guard]") {
  //  A supported Skyrim build paired with an unsupported SKSE version (or
  //  vice versa) must not be conflated into a single pass/fail signal here;
  //  the plugin entry point checks and reports both.
  CHECK(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
  CHECK_FALSE(IsSupportedSkseVersion(RuntimeVersion{2, 0, 20, 0}));
}
