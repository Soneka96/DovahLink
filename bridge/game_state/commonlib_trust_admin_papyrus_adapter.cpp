#include "game_state/commonlib_trust_admin_papyrus_adapter.hpp"

#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include <chrono>
#include <string_view>

namespace dovahlink::game_state {

namespace {

/// Non-owning pointer to the service every native function below forwards to,
/// set once by `InstallTrustAdminPapyrusAdapter` before the Papyrus VM can call
/// any of them. Papyrus native functions must be plain function pointers, not
/// captures, so this file-local pointer is the standard SKSE-ecosystem idiom
/// for reaching plugin-lifetime state from them.
application::TrustAdminService *g_trustAdminService = nullptr;

/// Result returned when a native function runs before
/// `InstallTrustAdminPapyrusAdapter` set `g_trustAdminService` -- unreachable
/// once the plugin has finished loading, kept only so a caller never gets a
/// null-dereference instead of a message.
constexpr const char *kUnavailableMessage =
    "DovahLink trust admin is unavailable.";

/// Result returned when `TrustAdminService` throws.
/// `ai/context/skse/cpp-style.md`: "never allow an exception to escape a
/// callback" -- a native Papyrus function is called directly by the game's
/// Papyrus VM, not through this codebase's own containment, so it is exactly
/// such a boundary.
constexpr const char *kInternalErrorMessage =
    "DovahLink trust admin failed unexpectedly.";

/// Native implementation of the Papyrus `DovahLinkAdmin.List(String)` function.
RE::BSFixedString List(RE::StaticFunctionTag *, RE::BSFixedString akScope) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(
        g_trustAdminService->List(std::string_view(akScope)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Revoke(String)`
/// function.
RE::BSFixedString Revoke(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(
        g_trustAdminService->RevokeByShortId(std::string_view(akId)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Reset()` function:
/// starts a Factory Reset confirmation challenge. Performs no mutation; the
/// destructive wipe happens only through `ConfirmReset` once the displayed code
/// is confirmed.
RE::BSFixedString Reset(RE::StaticFunctionTag *) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(g_trustAdminService->StartFactoryReset());
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Block(String)`
/// function.
RE::BSFixedString Block(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(g_trustAdminService->BlockByShortId(
        std::string_view(akId), std::chrono::steady_clock::now()));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Unblock(String)`
/// function.
RE::BSFixedString Unblock(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(
        g_trustAdminService->UnblockByShortId(std::string_view(akId)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Forget(String)`
/// function.
RE::BSFixedString Forget(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(
        g_trustAdminService->ForgetByShortId(std::string_view(akId)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.Help()` function.
RE::BSFixedString Help(RE::StaticFunctionTag *) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(g_trustAdminService->Help());
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.ConfirmReset(String)`
/// function: confirms a Factory Reset challenge started by `Reset`, executing
/// the destructive wipe on a matching code.
RE::BSFixedString ConfirmReset(RE::StaticFunctionTag *,
                               RE::BSFixedString akCode) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(
        g_trustAdminService->ConfirmFactoryReset(std::string_view(akCode)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Native implementation of the Papyrus `DovahLinkAdmin.ResetTrust()` function:
/// the recoverable, non-destructive bulk revoke -- unlike
/// `Reset`/`ConfirmReset`, requires no confirmation code.
RE::BSFixedString ResetTrust(RE::StaticFunctionTag *) {
  if (!g_trustAdminService) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return RE::BSFixedString(g_trustAdminService->ResetTrust());
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

/// Binds the nine native functions above to their Papyrus declarations.
bool RegisterFunctions(RE::BSScript::IVirtualMachine *vm) {
  vm->RegisterFunction("List", "DovahLinkAdmin", List);
  vm->RegisterFunction("Help", "DovahLinkAdmin", Help);
  vm->RegisterFunction("Revoke", "DovahLinkAdmin", Revoke);
  vm->RegisterFunction("Reset", "DovahLinkAdmin", Reset);
  vm->RegisterFunction("Block", "DovahLinkAdmin", Block);
  vm->RegisterFunction("Unblock", "DovahLinkAdmin", Unblock);
  vm->RegisterFunction("Forget", "DovahLinkAdmin", Forget);
  vm->RegisterFunction("ConfirmReset", "DovahLinkAdmin", ConfirmReset);
  vm->RegisterFunction("ResetTrust", "DovahLinkAdmin", ResetTrust);
  return true;
}

} // namespace

void InstallTrustAdminPapyrusAdapter(application::TrustAdminService &service) {
  g_trustAdminService = &service;

  // Unlike the plugin's messaging/serialization interfaces, this one backs a
  // purely optional feature (ai/context/protocol/security.md's "Trust
  // administration surface"): its absence disables only the console-admin
  // adapter, never the rest of the bridge, so this logs and returns rather than
  // failing plugin load.
  auto *papyrus = SKSE::GetPapyrusInterface();
  if (!papyrus) {
    SKSE::log::warn("SKSE's Papyrus interface is unavailable; the "
                    "trust-administration console "
                    "adapter will not be registered.");
    return;
  }
  if (!papyrus->Register(RegisterFunctions)) {
    SKSE::log::error("DovahLink trust-admin Papyrus function registration "
                     "failed; console commands "
                     "will remain unavailable.");
  }
}

} // namespace dovahlink::game_state
