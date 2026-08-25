#pragma once

#include "application/trust_device_admin_service.hpp"
#include "application/trust_reset_service.hpp"

namespace dovahlink::game_state {

///  Registers the native Papyrus functions
///  (`DovahLinkAdmin.List/Help/Revoke/Reset/Block/Unblock/Forget/ConfirmReset/ResetTrust`)
///  an optional ConsoleUtil Extended integration calls, forwarding device
///  commands to `deviceService` and reset commands to `resetService`. Carries
///  no trust logic of its own -- see
///  `ai/context/protocol/security.md`'s "Trust administration surface".
///  Registration is attempted unconditionally, independent of whether
///  ConsoleUtil Extended or its Papyrus glue script are actually installed; a
///  failure is logged and remains isolated to this optional adapter, while the
///  registered functions simply go unused if they are not.
///  @param deviceService Device listing and mutation service; must outlive the
///      Papyrus VM (in practice, the plugin's lifetime).
///  @param resetService Trust reset service; must outlive the Papyrus VM.
void InstallTrustAdminPapyrusAdapter(
    application::TrustDeviceAdminService& deviceService,
    application::TrustResetService& resetService);

} //  namespace dovahlink::game_state
