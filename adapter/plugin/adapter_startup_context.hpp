#pragma once

#include "constants.hpp"
#include "identity/adapter_instance_id.hpp"

#include <array>
#include <cstddef>
#include <filesystem>

namespace dovahlink::adapter::plugin {

///  The startup-only values `SKSEPluginLoad` resolves before constructing any
///  process-lifetime object, grouped because `AdapterRuntime`'s constructor
///  consumes all of them together as one startup contract.
struct AdapterStartupContext {
    ///  This connection instance's identity, generated once at startup.
    identity::AdapterInstanceId instanceId{};
    ///  The current Skyrim process lifetime's stable identity, reused by
    ///  rendezvous resolution, host launch, and DLL-detach shutdown signaling.
    std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId{};
    ///  Where the launched host publishes its rendezvous endpoint for this
    ///  process lifetime.
    std::filesystem::path rendezvousPath;
    ///  The packaged host executable's resolved path.
    std::filesystem::path hostExecutablePath;

    ///  Structural equality over every startup value.
    bool operator==(const AdapterStartupContext&) const = default;
};

} //  namespace dovahlink::adapter::plugin
