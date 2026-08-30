#include "process/adapter_host_endpoint_report.hpp"

#include <charconv>
#include <cstdint>
#include <system_error>
#include <vector>

namespace dovahlink::adapter::process {

namespace {

///  The value of a single lowercase hex digit, or `std::nullopt` if `digit`
///  is not one.
std::optional<std::uint8_t> LowercaseHexDigitValue(char digit) {
  if (digit >= '0' && digit <= '9') {
    return static_cast<std::uint8_t>(digit - '0');
  }
  if (digit >= 'a' && digit <= 'f') {
    return static_cast<std::uint8_t>(digit - 'a' + 10);
  }
  return std::nullopt;
}

///  Parses a lowercase hex string into owned bytes, or `std::nullopt` if it
///  has an odd length or contains a non-hex-digit character. An empty string
///  parses to an empty (but valid) byte vector.
std::optional<std::vector<std::byte>> ParseHexBytes(std::string_view text) {
  if (text.size() % 2 != 0) {
    return std::nullopt;
  }

  std::vector<std::byte> bytes(text.size() / 2);
  for (std::size_t index = 0; index < bytes.size(); ++index) {
    std::optional<std::uint8_t> high = LowercaseHexDigitValue(text[index * 2]);
    std::optional<std::uint8_t> low =
        LowercaseHexDigitValue(text[index * 2 + 1]);
    if (!high.has_value() || !low.has_value()) {
      return std::nullopt;
    }
    bytes[index] = static_cast<std::byte>((*high << 4) | *low);
  }
  return bytes;
}

///  Strips a single trailing `\r`, so a source written with Windows-style
///  `\r\n` line endings but split on `\n` alone does not carry a stray `\r`
///  into the parsed value.
std::string_view TrimTrailingCarriageReturn(std::string_view line) {
  if (!line.empty() && line.back() == '\r') {
    line.remove_suffix(1);
  }
  return line;
}

} //  namespace

std::optional<AdapterHostEndpoint>
TryParseHostEndpointReport(std::string_view portLine,
                           std::string_view proofLine) {
  portLine = TrimTrailingCarriageReturn(portLine);
  proofLine = TrimTrailingCarriageReturn(proofLine);

  constexpr std::string_view kPortPrefix = "PORT ";
  constexpr std::string_view kProofPrefix = "PROOF ";
  if (!portLine.starts_with(kPortPrefix) ||
      !proofLine.starts_with(kProofPrefix)) {
    return std::nullopt;
  }

  std::string_view portText = portLine.substr(kPortPrefix.size());
  int portValue = 0;
  auto [portEnd, portError] = std::from_chars(
      portText.data(), portText.data() + portText.size(), portValue);
  if (portError != std::errc{} ||
      portEnd != portText.data() + portText.size() || portValue < 0 ||
      portValue > 65535) {
    return std::nullopt;
  }

  std::string_view proofText = proofLine.substr(kProofPrefix.size());
  std::optional<std::vector<std::byte>> proofToken = ParseHexBytes(proofText);
  if (!proofToken.has_value()) {
    return std::nullopt;
  }

  return AdapterHostEndpoint{.port = static_cast<std::uint16_t>(portValue),
                             .proofToken = *proofToken};
}

} //  namespace dovahlink::adapter::process
