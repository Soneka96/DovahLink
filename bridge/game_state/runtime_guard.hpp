#pragma once

#include <cstdint>

namespace dovahlink::game_state {

/// Represents a four-part Skyrim or SKSE runtime version.
struct RuntimeVersion {
    /// Major version component.
    std::uint16_t major = 0;
    /// Minor version component.
    std::uint16_t minor = 0;
    /// Build or patch component.
    std::uint16_t build = 0;
    /// Revision component.
    std::uint16_t revision = 0;

    /// Compares all version components for equality.
    friend bool operator==(const RuntimeVersion&, const RuntimeVersion&) = default;
};

/// Supported Skyrim runtime version for plugin initialization.
inline constexpr RuntimeVersion kSupportedSkyrimVersion{1, 6, 1170, 0};
/// Supported SKSE runtime version for plugin initialization.
inline constexpr RuntimeVersion kSupportedSkseVersion{2, 2, 6, 0};

/// Returns whether `version` matches the supported Skyrim runtime.
[[nodiscard]] constexpr bool IsSupportedSkyrimVersion(const RuntimeVersion& version) {
    return version == kSupportedSkyrimVersion;
}

/// Returns whether `version` matches the supported SKSE runtime.
[[nodiscard]] constexpr bool IsSupportedSkseVersion(const RuntimeVersion& version) {
    return version == kSupportedSkseVersion;
}

// Compile-time insurance: the supported-version constants must satisfy
// their own predicate, catching a future edit that breaks this invariant at
// build time rather than only through a runtime test.
static_assert(IsSupportedSkyrimVersion(kSupportedSkyrimVersion));
static_assert(IsSupportedSkseVersion(kSupportedSkseVersion));

}  // namespace dovahlink::game_state
