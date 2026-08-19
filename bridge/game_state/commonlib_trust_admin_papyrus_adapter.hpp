#pragma once

#include "application/trust_admin_service.hpp"

namespace dovahlink::game_state {

/// Registers the native Papyrus functions
/// (`DovahLinkAdmin.List/Help/Revoke/Reset/Block/Unblock/Forget`)
/// an optional ConsoleUtil Extended integration calls, forwarding each to `service`. Carries no
/// trust logic of its own -- see `ai/context/protocol/security.md`'s "Trust administration
/// surface". Registration is attempted unconditionally, independent of whether ConsoleUtil
/// Extended or its Papyrus glue script are actually installed; a failure is logged and remains
/// isolated to this optional adapter, while the registered functions simply go unused if they are
/// not.
/// @param service Trust-admin service every native function forwards to; must outlive the Papyrus
///     VM (in practice, the plugin's lifetime).
void InstallTrustAdminPapyrusAdapter(application::TrustAdminService& service);

}  // namespace dovahlink::game_state
