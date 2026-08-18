#pragma once

#include "security/enums.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::security {

/// Required length of the one-time authentication token in bytes.
inline constexpr std::size_t kTokenBytes = 32;

/// Abstracts process-environment access for token loading.
class EnvironmentReader {
public:
    /// Allows destruction through the interface.
    virtual ~EnvironmentReader() = default;

    /// Returns an environment value or `std::nullopt` when it is unset.
    [[nodiscard]] virtual std::optional<std::string> Read(std::string_view name) const = 0;
};

/// Reads environment values through the Windows process environment block.
class WindowsEnvironmentReader : public EnvironmentReader {
public:
    /// @copydoc EnvironmentReader::Read
    [[nodiscard]] std::optional<std::string> Read(std::string_view name) const override;
};

/// Result of reading and hex-decoding the developer-authentication token from an environment value.
struct TokenReadResult {
    /// Which of the three documented outcomes occurred.
    TokenReadOutcome outcome;
    /// Decoded token bytes; empty unless `outcome == TokenReadOutcome::kValid`.
    std::vector<std::uint8_t> bytes;
};

/// Reads and hex-decodes a 256-bit one-time token from an environment value,
/// distinguishing a missing variable from a malformed one so callers can log
/// and react to each differently. Intermediate plaintext is cleared on every
/// normal and exceptional exit.
[[nodiscard]] TokenReadResult ReadTokenFromEnvironment(const EnvironmentReader& env,
                                                        std::string_view variableName);

}  // namespace dovahlink::security
