#pragma once

#include "security/token_read_result.hpp"

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::security {

///  Required length of the one-time authentication token in bytes.
inline constexpr std::size_t kTokenBytes = 32;

///  Abstracts process-environment access for token loading.
class IEnvironmentReader {
  public:
    ///  Allows destruction through the interface.
    virtual ~IEnvironmentReader() = default;

    ///  Returns an environment value or `std::nullopt` when it is unset.
    [[nodiscard]] virtual std::optional<std::string>
    Read(std::string_view name) const = 0;
};

///  Reads environment values through the Windows process environment block.
class WindowsEnvironmentReader : public IEnvironmentReader {
  public:
    ///  @copydoc IEnvironmentReader::Read
    [[nodiscard]] std::optional<std::string>
    Read(std::string_view name) const override;
};

///  Reads and hex-decodes a 256-bit one-time token from an environment value,
///  distinguishing a missing variable from a malformed one so callers can log
///  and react to each differently. Intermediate plaintext is cleared on every
///  normal and exceptional exit.
[[nodiscard]] TokenReadResult
ReadTokenFromEnvironment(const IEnvironmentReader& env,
                         std::string_view variableName);

} //  namespace dovahlink::security
