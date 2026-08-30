#pragma once

#include "ipc/ipc_constants.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <array>
#include <cstddef>
#include <filesystem>
#include <optional>

namespace dovahlink::adapter::process {

///  Resolves the per-user, per-lifetime rendezvous file a host process
///  publishes its currently bound port and peer-proof token to, matching the
///  host's own `Constants.RendezvousFilePath`.
///  @param ownerLifetimeId The owning Skyrim process's lifetime identity.
///  @return The resolved path, or `std::nullopt` if `LOCALAPPDATA` is unset
///  for the current Windows user session.
std::optional<std::filesystem::path> ResolveDefaultRendezvousFilePath(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>
        &ownerLifetimeId);

///  Reads a candidate host endpoint from a rendezvous file. Discovery only,
///  never authentication: a missing, stale, or malformed file is never an
///  error -- it simply yields no candidate, and any candidate this returns
///  must still pass the full mutual Hello/HelloAck handshake before it is
///  trusted.
class IAdapterHostRendezvousReader {
public:
  virtual ~IAdapterHostRendezvousReader() = default;

  ///  Reads the configured rendezvous file.
  ///  @return The candidate endpoint, or `std::nullopt` if the file is
  ///  missing or its content is not well-formed.
  virtual std::optional<AdapterHostEndpoint> TryRead() = 0;
};

///  @copydoc IAdapterHostRendezvousReader
class FileAdapterHostRendezvousReader final
    : public IAdapterHostRendezvousReader {
public:
  ///  Creates a reader for an explicit rendezvous file path.
  ///  @param filePath The rendezvous file to read, typically
  ///  `ResolveDefaultRendezvousFilePath`'s result for the current Skyrim
  ///  lifetime.
  explicit FileAdapterHostRendezvousReader(std::filesystem::path filePath);

  ///  @copydoc IAdapterHostRendezvousReader::TryRead
  std::optional<AdapterHostEndpoint> TryRead() override;

private:
  ///  The rendezvous file this instance reads.
  std::filesystem::path filePath_;
};

} //  namespace dovahlink::adapter::process
