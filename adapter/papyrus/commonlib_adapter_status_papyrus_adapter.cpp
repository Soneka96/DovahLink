#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include "papyrus/commonlib_adapter_status_papyrus_adapter.hpp"

namespace dovahlink::adapter::papyrus {

namespace {

///  Non-owning pointer to the session below, set once by
///  `InstallAdapterStatusPapyrusAdapter` before the Papyrus VM can call any
///  native function. Papyrus native functions must be plain function
///  pointers, not captures, so this file-local pointer is the standard
///  SKSE-ecosystem idiom for reaching plugin-lifetime state from them.
ipc::IAdapterIpcSession *g_session = nullptr;

///  Result returned when a native function runs before
///  `InstallAdapterStatusPapyrusAdapter` set the session pointer --
///  unreachable once the plugin has finished loading, kept only so a caller
///  never gets a null-dereference instead of a message.
constexpr const char *kUnavailableMessage =
    "DovahLink adapter status is unavailable.";

///  Result returned when querying the session throws.
///  `ai/context/skse/cpp-style.md`: "never allow an exception to escape a
///  callback" -- a native Papyrus function is called directly by the game's
///  Papyrus VM, not through this codebase's own containment, so it is
///  exactly such a boundary.
constexpr const char *kInternalErrorMessage =
    "DovahLink adapter status check failed unexpectedly.";

///  Result returned when the host is not currently available.
constexpr const char *kHostNotReadyMessage = "host not ready";

///  Result returned when the host is currently available.
constexpr const char *kHostReadyMessage = "host ready";

///  Native implementation of the Papyrus
///  `DovahLinkAdapterStatus.GetHostStatus()` function.
RE::BSFixedString GetHostStatus(RE::StaticFunctionTag *) {
  if (!g_session) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(g_session->IsHostAvailable()
                                 ? kHostReadyMessage
                                 : kHostNotReadyMessage);
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

///  Binds the native function above to its Papyrus declaration.
bool RegisterFunctions(RE::BSScript::IVirtualMachine *vm) {
  vm->RegisterFunction("GetHostStatus", "DovahLinkAdapterStatus",
                       GetHostStatus);
  return true;
}

} //  namespace

void InstallAdapterStatusPapyrusAdapter(ipc::IAdapterIpcSession &session) {
  g_session = &session;

  //  Unlike the plugin's messaging/serialization interfaces, this one backs
  //  a purely optional Skyrim-facing status surface: its absence disables
  //  only this query, never the rest of the adapter, so this logs and
  //  returns rather than failing plugin load.
  auto *papyrusInterface = SKSE::GetPapyrusInterface();
  if (!papyrusInterface) {
    SKSE::log::warn("SKSE's Papyrus interface is unavailable; the adapter "
                    "status surface will not be registered.");
    return;
  }
  if (!papyrusInterface->Register(RegisterFunctions)) {
    SKSE::log::error("DovahLink adapter status Papyrus function "
                     "registration failed; the status query will remain "
                     "unavailable.");
  }
}

} //  namespace dovahlink::adapter::papyrus
