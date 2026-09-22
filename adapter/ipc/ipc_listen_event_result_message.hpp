#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the adapter in response to `IpcListenEventMessage`, reporting
///  whether the event key now has, or already had, an approved persistent
///  registration. See `IAdapterNativeCaptureRouter::RegisterEvent`'s own
///  documentation for the registration's idempotence contract.
struct IpcListenEventResultMessage {
    ///  Matches the `IpcListenEventMessage` this responds to.
    std::uint64_t correlationId = 0;
    ///  Whether the event key was accepted for registration.
    bool accepted = false;

    ///  Structural equality over every field.
    bool operator==(const IpcListenEventResultMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc
