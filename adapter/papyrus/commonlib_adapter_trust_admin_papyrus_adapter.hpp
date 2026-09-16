#pragma once

#include "ipc/adapter_ipc_session.hpp"
#include "runtime/adapter_task_marshaller.hpp"

namespace dovahlink::adapter::papyrus {

///  Registers the native Papyrus functions
///  (`DovahLinkAdmin.List/Help/Revoke/Block/Unblock/Forget/ResetTrust/Reset/ConfirmReset`)
///  an optional ConsoleUtil Extended integration calls, forwarding every
///  command to `session`'s `SendTrustAdminRequest` -- the host remains the
///  sole trust-administration authority, and this adapter performs no trust
///  logic of its own. The `DovahLinkAdmin` Papyrus class and function names
///  are a stable external contract with ConsoleUtil Extended's own glue
///  script and YAML config, which reference them by these exact names. See
///  `ai/context/protocol/security.md`'s "Trust administration surface".
///  Registration is attempted unconditionally,
///  independent of whether ConsoleUtil Extended or its Papyrus glue script
///  are actually installed; a failure is logged and remains isolated to this
///  optional adapter, and the registered functions simply go unused if they
///  are not.
///  @param session Session every command is forwarded through; must outlive
///  the Papyrus VM (in practice, the plugin's lifetime).
///  @param marshaller Schedules every latent function's terminal
///  `ReturnLatentResult` call onto the game thread -- the one thread
///  Skyrim's scripting VM supports -- for every outcome this adapter itself
///  decides locally (an unavailable session, malformed input, or a
///  synchronous exception), the same invariant `session`'s own
///  `SendTrustAdminRequest`-originated outcomes already satisfy through
///  `DispatchTrustAdminCompletion`. Must outlive the Papyrus VM, the same as
///  `session`.
void InstallAdapterTrustAdminPapyrusAdapter(
    ipc::IAdapterIpcSession& session,
    runtime::IAdapterTaskMarshaller& marshaller);

} //  namespace dovahlink::adapter::papyrus
