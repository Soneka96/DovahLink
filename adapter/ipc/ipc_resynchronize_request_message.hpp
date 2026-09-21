#pragma once

#include <cstdint>
#include <vector>

namespace dovahlink::adapter::ipc {

///  Sent by the host with the bounded event registrations and baseline samples
///  the adapter must execute to establish a fresh current-state baseline.
///  The host plan removes duplicates in first-seen order; the wire lists
///  preserve their supplied order and can carry repeated values.
struct IpcResynchronizeRequestMessage {
    ///  Pairs this request with its `IpcResynchronizeResultMessage` response.
    std::uint64_t correlationId = 0;

    ///  The ordered event keys to register before sampling.
    std::vector<std::uint32_t> persistentEventKeys;

    ///  The ordered baseline sample tokens to capture after event registration.
    std::vector<std::uint32_t> baselineSampleTokens;

    ///  Structural equality over every field.
    bool operator==(const IpcResynchronizeRequestMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc
