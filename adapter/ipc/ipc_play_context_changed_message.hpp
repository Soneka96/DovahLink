#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the adapter to notify the host of a new Skyrim play context (one
///  save/load lifetime): starting a new game, loading a save, or announcing
///  the context already current after a reconnect. Best effort and
///  unsolicited: the host sends no reply.
struct IpcPlayContextChangedMessage {
    ///  Always zero; this notification is unsolicited and expects no reply.
    std::uint64_t correlationId = 0;
    ///  The adapter-generated play-context identity, as 16 opaque bytes.
    std::array<std::byte, 16> playContextId{};

    ///  Structural equality over every field.
    bool operator==(const IpcPlayContextChangedMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc
