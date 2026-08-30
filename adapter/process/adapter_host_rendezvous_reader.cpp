#include "process/adapter_host_rendezvous_reader.hpp"

#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <charconv>
#include <fstream>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>

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

///  Strips a single trailing `\r`, so a file written with Windows-style
///  `\r\n` line endings and read via `std::getline` (which only splits on
///  `\n`) does not carry a stray `\r` into the parsed value.
void TrimTrailingCarriageReturn(std::string &line) {
  if (!line.empty() && line.back() == '\r') {
    line.pop_back();
  }
}

} //  namespace

std::optional<std::filesystem::path> ResolveDefaultRendezvousFilePath(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>
        &ownerLifetimeId) {
  constexpr DWORD kMaxEnvValueChars = 4096;
  std::wstring buffer(kMaxEnvValueChars, L'\0');
  DWORD written = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer.data(),
                                          kMaxEnvValueChars);
  if (written == 0 || written >= kMaxEnvValueChars) {
    return std::nullopt;
  }
  buffer.resize(written);

  std::filesystem::path path(buffer);
  path /= L"DovahLink";
  path /= L"host";
  path /= "rendezvous-" + FormatOwnerLifetimeId(ownerLifetimeId) + ".dat";
  return path;
}

FileAdapterHostRendezvousReader::FileAdapterHostRendezvousReader(
    std::filesystem::path filePath)
    : filePath_(std::move(filePath)) {}

std::optional<AdapterHostEndpoint> FileAdapterHostRendezvousReader::TryRead() {
  std::ifstream file(filePath_);
  if (!file.is_open()) {
    return std::nullopt;
  }

  std::string portLine;
  std::string proofLine;
  if (!std::getline(file, portLine) || !std::getline(file, proofLine)) {
    return std::nullopt;
  }
  TrimTrailingCarriageReturn(portLine);
  TrimTrailingCarriageReturn(proofLine);

  constexpr std::string_view kPortPrefix = "PORT ";
  constexpr std::string_view kProofPrefix = "PROOF ";
  if (!portLine.starts_with(kPortPrefix) ||
      !proofLine.starts_with(kProofPrefix)) {
    return std::nullopt;
  }

  std::string_view portText(portLine);
  portText.remove_prefix(kPortPrefix.size());
  int portValue = 0;
  auto [portEnd, portError] = std::from_chars(
      portText.data(), portText.data() + portText.size(), portValue);
  if (portError != std::errc{} ||
      portEnd != portText.data() + portText.size() || portValue < 0 ||
      portValue > 65535) {
    return std::nullopt;
  }

  std::string_view proofText(proofLine);
  proofText.remove_prefix(kProofPrefix.size());
  std::optional<std::vector<std::byte>> proofToken = ParseHexBytes(proofText);
  if (!proofToken.has_value()) {
    return std::nullopt;
  }

  return AdapterHostEndpoint{.port = static_cast<std::uint16_t>(portValue),
                             .proofToken = *proofToken};
}

} //  namespace dovahlink::adapter::process
