#pragma once

#include <variant>

#include "ipc/ipc_cancel_message.hpp"
#include "ipc/ipc_close_message.hpp"
#include "ipc/ipc_hello_ack_message.hpp"
#include "ipc/ipc_hello_message.hpp"
#include "ipc/ipc_listen_event_message.hpp"
#include "ipc/ipc_read_sample_message.hpp"
#include "ipc/ipc_reject_message.hpp"
#include "ipc/ipc_resynchronize_request_message.hpp"
#include "ipc/ipc_resynchronize_result_message.hpp"

namespace dovahlink::adapter::ipc {

///  A decoded or to-be-encoded private host-to-adapter IPC message envelope
///  value. Every alternative is an owned plain value; none may retain a
///  Skyrim/CommonLib pointer, borrowed buffer, or public-protocol object.
using IpcMessage =
    std::variant<IpcHelloMessage, IpcHelloAckMessage,
                 IpcResynchronizeRequestMessage, IpcResynchronizeResultMessage,
                 IpcCloseMessage, IpcRejectMessage, IpcCancelMessage,
                 IpcListenEventMessage, IpcReadSampleMessage>;

} //  namespace dovahlink::adapter::ipc
