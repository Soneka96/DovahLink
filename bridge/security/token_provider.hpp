#pragma once

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

/// Reads and hex-decodes a 256-bit one-time token from an environment value.
/// Invalid, empty, unset, or incorrectly sized values return `std::nullopt`;
/// intermediate text is cleared before returning.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> ReadTokenFromEnvironment(
    const EnvironmentReader& env, std::string_view variableName);

}  // namespace dovahlink::security
