#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Sent by the adapter to notify the host that the current play context has
///  ended: loading has started (before the new context is established), or
///  the player returned to the main menu. Best effort and unsolicited: the
///  host sends no reply. No new context is established until a later
///  `IpcPlayContextChangedMessage`.
struct IpcPlayContextEndedMessage {
    ///  Always zero; this notification is unsolicited and expects no reply.
    std::uint64_t correlationId = 0;

    ///  Structural equality over every field.
    bool operator==(const IpcPlayContextEndedMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc
