#pragma once

#include "ipc/adapter_ipc_session.hpp"

namespace dovahlink::adapter::papyrus {

///  Registers the native Papyrus functions
///  (`DovahLinkAdmin.List/Help/Revoke/Block/Unblock/Forget/ResetTrust/Reset/ConfirmReset`)
///  an optional ConsoleUtil Extended integration calls, forwarding every
///  command to `session`'s `SendTrustAdminRequest` -- the host remains the
///  sole trust-administration authority, and this adapter performs no trust
///  logic of its own. Reuses the frozen `bridge/` implementation's exact
///  Papyrus class and function names (see
///  `bridge/game_state/commonlib_trust_admin_papyrus_adapter.hpp`) so the
///  same optional glue script and ConsoleUtil Extended YAML config work
///  unchanged against either. See `ai/context/protocol/security.md`'s "Trust
///  administration surface". Registration is attempted unconditionally,
///  independent of whether ConsoleUtil Extended or its Papyrus glue script
///  are actually installed; a failure is logged and remains isolated to this
///  optional adapter, and the registered functions simply go unused if they
///  are not.
///  @param session Session every command is forwarded through; must outlive
///  the Papyrus VM (in practice, the plugin's lifetime).
void InstallAdapterTrustAdminPapyrusAdapter(ipc::IAdapterIpcSession &session);

} //  namespace dovahlink::adapter::papyrus
