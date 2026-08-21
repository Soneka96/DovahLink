#pragma once

#if defined(_WIN32)
#include <windows.h>
#endif

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

/// Returns whether the current Windows runtime meets the Bridge's minimum OS requirement.
[[nodiscard]] inline bool IsCurrentWindowsVersionSupported() noexcept {
#if defined(_WIN32)
    // VersionHelpers uses the host process manifest. SKSE loads this DLL into Skyrim, whose
    // manifest is outside the Bridge's control, so use the kernel's version directly instead.
    auto* ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll == nullptr) {
        return false;
    }
    auto* getVersion = reinterpret_cast<LONG(WINAPI*)(OSVERSIONINFOEXW*)>(GetProcAddress(ntdll, "RtlGetVersion"));
    if (getVersion == nullptr) {
        return false;
    }
    OSVERSIONINFOEXW version{};
    version.dwOSVersionInfoSize = sizeof(version);
    if (getVersion(&version) != 0) {
        return false;
    }
    return version.dwMajorVersion >= 10;
#else
    // The native Bridge target is Windows-only. Keep the neutral test target
    // usable on non-Windows hosts without pretending to validate Windows there.
    return true;
#endif
}

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
