#include "process/adapter_host_rendezvous_reader.hpp"

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

  return TryParseHostEndpointReport(portLine, proofLine);
}

} //  namespace dovahlink::adapter::process
