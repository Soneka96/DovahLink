#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <variant>
#include <vector>

#include "enums.hpp"
#include "ipc/ipc_cancel_message.hpp"
#include "ipc/ipc_close_message.hpp"
#include "ipc/ipc_hello_ack_message.hpp"
#include "ipc/ipc_hello_message.hpp"
#include "ipc/ipc_listen_event_message.hpp"
#include "ipc/ipc_pairing_attempts_exhausted_message.hpp"
#include "ipc/ipc_pairing_display_ack_message.hpp"
#include "ipc/ipc_pairing_display_message.hpp"
#include "ipc/ipc_read_sample_message.hpp"
#include "ipc/ipc_reject_message.hpp"
#include "ipc/ipc_resynchronize_request_message.hpp"
#include "ipc/ipc_resynchronize_result_message.hpp"
#include "ipc/ipc_trust_admin_request_message.hpp"
#include "ipc/ipc_trust_admin_result_message.hpp"

namespace dovahlink::adapter::ipc {

//  TODO(stage4-file-extraction): Move IpcCaptureResultMessage to its own
//  ipc/ipc_capture_result_message.hpp in the post-Stage-4 structural cleanup
//  PR. Temporarily colocated here to hold this PR's changed-file count down;
//  extraction only, no behavior change.
///  Reports one captured value, or its unavailability, to the host.
///  `correlationId` matches the originating `IpcReadSampleMessage` for a
///  sampled capture, or is zero for a capture with no originating host
///  request (for example a future spontaneous native-event capture).
struct IpcCaptureResultMessage {
    ///  Matches the originating request, or zero. See the type documentation.
    std::uint64_t correlationId = 0;
    ///  Which host-owned key namespace `captureKey` belongs to.
    capture::CaptureSourceKind source = capture::CaptureSourceKind::kSample;
    ///  The host-owned sample token or event key this result was captured for.
    std::uint32_t captureKey = 0;
    ///  Whether `payload` holds a real captured value.
    capture::CaptureAvailability availability =
        capture::CaptureAvailability::kAvailable;
    ///  The play context that was current at the moment this value was
    ///  captured, matching `capture::AdapterCaptureWorkItem::playContextId`.
    std::array<std::byte, 16> playContextId{};
    ///  The captured value, already copied out of Skyrim state; empty when
    ///  `availability` is `kUnavailable`.
    std::vector<std::byte> payload;

    ///  Structural equality over every field.
    bool operator==(const IpcCaptureResultMessage&) const = default;
};

//  TODO(stage4-file-extraction): Move IpcListenEventResultMessage to its own
//  ipc/ipc_listen_event_result_message.hpp in the post-Stage-4 structural
//  cleanup PR. Temporarily colocated here to hold this PR's changed-file count
//  down; extraction only, no behavior change.
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

//  TODO(stage4-file-extraction): Move IpcPlayContextChangedMessage to its own
//  ipc/ipc_play_context_changed_message.hpp in the post-Stage-4 structural
//  cleanup PR. Temporarily colocated here to hold this PR's changed-file count
//  down; extraction only, no behavior change.
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

//  TODO(stage4-file-extraction): Move IpcPlayContextEndedMessage to its own
//  ipc/ipc_play_context_ended_message.hpp in the post-Stage-4 structural
//  cleanup PR. Temporarily colocated here to hold this PR's changed-file count
//  down; extraction only, no behavior change.
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

///  A decoded or to-be-encoded private host-to-adapter IPC message envelope
///  value. Every alternative is an owned plain value; none may retain a
///  Skyrim/CommonLib pointer, borrowed buffer, or public-protocol object.
using IpcMessage =
    std::variant<IpcHelloMessage, IpcHelloAckMessage,
                 IpcResynchronizeRequestMessage, IpcResynchronizeResultMessage,
                 IpcCloseMessage, IpcRejectMessage, IpcCancelMessage,
                 IpcListenEventMessage, IpcReadSampleMessage,
                 IpcPairingDisplayMessage, IpcPairingDisplayAckMessage,
                 IpcPairingAttemptsExhaustedMessage,
                 IpcTrustAdminRequestMessage, IpcTrustAdminResultMessage,
                 IpcCaptureResultMessage, IpcListenEventResultMessage,
                 IpcPlayContextChangedMessage, IpcPlayContextEndedMessage>;

} //  namespace dovahlink::adapter::ipc
