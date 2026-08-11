#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::security {

// The one-time token is a 256-bit value (TASK.md: "Use a cryptographically
// random 256-bit one-time token for the Phase 1 proof"), supplied hex-encoded
// (64 hex characters) through the launch environment.
inline constexpr std::size_t kTokenBytes = 32;

// Reads a single named value from the process's environment. Exists purely
// as a seam: the real implementation touches the Windows environment block,
// which is not something a unit test should depend on (ai/context/skse/
// testing.md: application-adjacent code must be testable without relying on
// a real OS/runtime side channel). Tests use a fake.
class EnvironmentReader {
public:
    virtual ~EnvironmentReader() = default;

    // Returns nullopt if `name` is not set in the environment.
    [[nodiscard]] virtual std::optional<std::string> Read(std::string_view name) const = 0;
};

// The real environment reader, backed by the Win32 environment block.
class WindowsEnvironmentReader : public EnvironmentReader {
public:
    [[nodiscard]] std::optional<std::string> Read(std::string_view name) const override;
};

// Reads `variableName` from `env` and decodes it as the one-time token.
// Returns nullopt if the variable is unset, empty, contains characters other
// than hex digits, or does not decode to exactly kTokenBytes bytes -- every
// case is treated the same way (no trustworthy token available), matching
// game_state::CaptureLevel's "missing or invalid means unavailable, not a
// plausible default" pattern (ai/context/protocol/conventions.md). Never
// logs the raw or decoded value, and clears the intermediate hex text before
// returning; the caller must not log the returned bytes either (ai/context/
// protocol/security.md: "never commit, persist, log, echo... a real
// token").
[[nodiscard]] std::optional<std::vector<std::uint8_t>> ReadTokenFromEnvironment(
    const EnvironmentReader& env, std::string_view variableName);

}  // namespace dovahlink::security
