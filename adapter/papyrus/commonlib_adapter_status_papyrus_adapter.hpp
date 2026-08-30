#pragma once

#include "ipc/adapter_ipc_session.hpp"

namespace dovahlink::adapter::papyrus {

///  Registers the native Papyrus function
///  `DovahLinkAdapterStatus.GetHostStatus()`, forwarding to `session`'s
///  `IsHostAvailable()`. Thin status forwarding only -- no retry, pairing,
///  or business policy: this query never itself triggers or waits on a
///  connection attempt, it only reads the session's already-tracked
///  availability. Registration is attempted unconditionally; a failure is
///  logged and the registered function simply goes unused if no Papyrus
///  script declares it.
///  @param session Session queried for host availability; must outlive the
///  Papyrus VM (in practice, the plugin's lifetime).
void InstallAdapterStatusPapyrusAdapter(ipc::IAdapterIpcSession &session);

} //  namespace dovahlink::adapter::papyrus
