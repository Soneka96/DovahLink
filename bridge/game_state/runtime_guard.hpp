#pragma once

#include <cstdint>

namespace dovahlink::game_state {

// A four-part version number (major.minor.build.revision), matching the
// scheme Bethesda/SKSE use for the Skyrim executable; SKSE's own version
// leaves revision at 0 (SKSE uses major.minor.patch). Skyrim-independent: no
// CommonLib/RE:: type appears here, so the supported-runtime decision can be
// tested without Skyrim (ai/context/skse/testing.md: "Test game-state
// conversion with plain representative values; do not require a running
// Skyrim process").
struct RuntimeVersion {
    std::uint16_t major = 0;
    std::uint16_t minor = 0;
    std::uint16_t build = 0;
    std::uint16_t revision = 0;

    friend bool operator==(const RuntimeVersion&, const RuntimeVersion&) = default;
};

// The only Skyrim runtime and SKSE combination Phase 1 supports (TASK.md).
// Building with a multi-runtime-capable library
// (commonlibsse-ng-flatrim, see bridge/README.md) does not make another
// runtime supported: every other combination is rejected clearly during
// plugin initialization, and nothing else runs (ai/context/skse/
// architecture.md: "Reject unsupported runtimes clearly during plugin
// initialization"). The actual query of the running Skyrim/SKSE version
// requires CommonLib and is added in the SKSE plugin entry point step; this
// file only holds the Skyrim-independent supported/unsupported decision.
inline constexpr RuntimeVersion kSupportedSkyrimVersion{1, 6, 1170, 0};
inline constexpr RuntimeVersion kSupportedSkseVersion{2, 2, 6, 0};

/**
 * @brief Determines whether a runtime version is the supported Skyrim version.
 *
 * @param version Runtime version to check.
 * @return `true` if the version matches the supported Skyrim version, `false` otherwise.
 */
[[nodiscard]] constexpr bool IsSupportedSkyrimVersion(const RuntimeVersion& version) {
    return version == kSupportedSkyrimVersion;
}

/**
 * @brief Determines whether a runtime version is the supported SKSE version.
 *
 * @param version Runtime version to evaluate.
 * @return `true` if the version matches the supported SKSE version, `false` otherwise.
 */
[[nodiscard]] constexpr bool IsSupportedSkseVersion(const RuntimeVersion& version) {
    return version == kSupportedSkseVersion;
}

// Compile-time insurance: the supported-version constants must satisfy
// their own predicate, catching a future edit that breaks this invariant at
// build time rather than only through a runtime test.
static_assert(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
static_assert(IsSupportedSkseVersion(kSupportedSkseVersion));

}  // namespace dovahlink::game_state
