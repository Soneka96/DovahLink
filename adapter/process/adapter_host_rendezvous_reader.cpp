#include "process/adapter_host_rendezvous_reader.hpp"

#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_endpoint_report.hpp"
#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <fstream>
#include <string>
#include <utility>

namespace dovahlink::adapter::process {

namespace {

///  Reads one newline-terminated or final line without allowing an input file
///  to grow the string beyond the small rendezvous-report bound.
std::optional<std::string> ReadBoundedLine(std::istream &file) {
  std::string line;
  line.reserve(kMaxAdapterHostRendezvousLineBytes);
  bool readAny = false;
  while (line.size() <= kMaxAdapterHostRendezvousLineBytes) {
    int value = file.get();
    if (value == '\n') {
      return line;
    }
    if (value == std::char_traits<char>::eof()) {
      return readAny ? std::optional<std::string>{std::move(line)}
                     : std::nullopt;
    }
    readAny = true;
    if (line.size() == kMaxAdapterHostRendezvousLineBytes) {
      return std::nullopt;
    }
    line.push_back(static_cast<char>(value));
  }
  return std::nullopt;
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

  std::optional<std::string> portLine = ReadBoundedLine(file);
  std::optional<std::string> proofLine = ReadBoundedLine(file);
  if (!portLine.has_value() || !proofLine.has_value()) {
    return std::nullopt;
  }

  return TryParseHostEndpointReport(*portLine, *proofLine);
}

} //  namespace dovahlink::adapter::process
