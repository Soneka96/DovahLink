#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include "papyrus/commonlib_adapter_trust_admin_papyrus_adapter.hpp"

#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_enums.hpp"

#include <algorithm>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::adapter::papyrus {

namespace {

///  Non-owning pointer to the session below, set once by
///  `InstallAdapterTrustAdminPapyrusAdapter` before the Papyrus VM can call
///  any native function. Papyrus native functions must be plain function
///  pointers, not captures, so this file-local pointer is the standard
///  SKSE-ecosystem idiom for reaching plugin-lifetime state from them.
ipc::IAdapterIpcSession *g_session = nullptr;

///  Result returned when a native function runs before
///  `InstallAdapterTrustAdminPapyrusAdapter` set the session pointer --
///  unreachable once the plugin has finished loading, kept only so a caller
///  never gets a null-dereference instead of a message.
constexpr const char *kUnavailableMessage =
    "DovahLink trust admin is unavailable.";

///  Result returned when `IAdapterIpcSession::SendTrustAdminRequest` reports
///  no result: no authenticated host connection, the request could not be
///  sent, or no correlated result arrived within its bound.
constexpr const char *kHostNotReadyMessage = "host not ready";

///  Result returned when a native function throws.
///  `ai/context/skse/cpp-style.md`: "never allow an exception to escape a
///  callback" -- a native Papyrus function is called directly by the game's
///  Papyrus VM, not through this codebase's own containment, so it is
///  exactly such a boundary.
constexpr const char *kInternalErrorMessage =
    "DovahLink trust admin failed unexpectedly.";

///  Result returned when `List`'s scope argument is not one of "all",
///  "trust", or "block".
constexpr const char *kUnrecognizedScopeMessage = "Unrecognized list scope.";

///  Result returned when a short-id-targeted command's argument is not
///  exactly `ipc::kPairingShortIdDigits` ASCII decimal digits.
constexpr const char *kInvalidShortIdMessage = "Invalid device id.";

///  Result returned when `ConfirmReset`'s argument is not exactly
///  `ipc::kFactoryResetChallengeCodeDigits` ASCII decimal digits.
constexpr const char *kInvalidConfirmationCodeMessage =
    "Invalid confirmation code.";

///  Whether every character in `text` is an ASCII decimal digit and `text`
///  is exactly `expectedLength` characters long.
bool IsFixedAsciiDigits(std::string_view text, std::size_t expectedLength) {
  return text.size() == expectedLength &&
         std::ranges::all_of(text, [](char c) { return c >= '0' && c <= '9'; });
}

///  Parses `List`'s scope argument into its closed wire value.
std::optional<ipc::TrustAdminListScope> ParseListScope(std::string_view scope) {
  if (scope == "all") {
    return ipc::TrustAdminListScope::kAll;
  }
  if (scope == "trust") {
    return ipc::TrustAdminListScope::kTrust;
  }
  if (scope == "block") {
    return ipc::TrustAdminListScope::kBlock;
  }
  return std::nullopt;
}

///  Formats a `SendTrustAdminRequest` result, or the controlled host-not-ready
///  message when it reports none.
RE::BSFixedString FormatResult(std::optional<std::string> result) {
  return RE::BSFixedString(result.has_value() ? result->c_str()
                                              : kHostNotReadyMessage);
}

///  Sends one no-argument trust-admin request and formats the result. Shared
///  by `Help`, `ResetTrust`, and `Reset`: each is itself the
///  framework-mandated plain-function signature SKSE's Papyrus binding
///  requires, and this ordinary helper reaches plugin-lifetime state through
///  the same file-local `g_session` pointer those functions already do,
///  rather than accepting it as a parameter.
RE::BSFixedString SendNoArgument(ipc::TrustAdminOperation operation) {
  if (!g_session) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  try {
    return FormatResult(g_session->SendTrustAdminRequest(operation));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

///  Sends one short-id-targeted trust-admin request and formats the result,
///  rejecting a malformed id before it ever reaches the host.
RE::BSFixedString SendWithShortId(ipc::TrustAdminOperation operation,
                                  RE::BSFixedString akId) {
  if (!g_session) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  std::string_view shortId(akId);
  if (!IsFixedAsciiDigits(shortId, ipc::kPairingShortIdDigits)) {
    return RE::BSFixedString(kInvalidShortIdMessage);
  }
  try {
    return FormatResult(g_session->SendTrustAdminRequest(
        operation, std::nullopt, std::string(shortId)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

///  Native implementation of the Papyrus `DovahLinkAdmin.List(String)`
///  function.
RE::BSFixedString List(RE::StaticFunctionTag *, RE::BSFixedString akScope) {
  if (!g_session) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  std::optional<ipc::TrustAdminListScope> scope =
      ParseListScope(std::string_view(akScope));
  if (!scope.has_value()) {
    return RE::BSFixedString(kUnrecognizedScopeMessage);
  }
  try {
    return FormatResult(g_session->SendTrustAdminRequest(
        ipc::TrustAdminOperation::kList, *scope));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Help()` function.
RE::BSFixedString Help(RE::StaticFunctionTag *) {
  return SendNoArgument(ipc::TrustAdminOperation::kHelp);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Revoke(String)`
///  function.
RE::BSFixedString Revoke(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  return SendWithShortId(ipc::TrustAdminOperation::kRevoke, akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Block(String)`
///  function.
RE::BSFixedString Block(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  return SendWithShortId(ipc::TrustAdminOperation::kBlock, akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Unblock(String)`
///  function.
RE::BSFixedString Unblock(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  return SendWithShortId(ipc::TrustAdminOperation::kUnblock, akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Forget(String)`
///  function.
RE::BSFixedString Forget(RE::StaticFunctionTag *, RE::BSFixedString akId) {
  return SendWithShortId(ipc::TrustAdminOperation::kForget, akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.ResetTrust()`
///  function: the recoverable, non-destructive bulk revoke -- unlike
///  `Reset`/`ConfirmReset`, requires no confirmation code.
RE::BSFixedString ResetTrust(RE::StaticFunctionTag *) {
  return SendNoArgument(ipc::TrustAdminOperation::kResetTrust);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Reset()` function:
///  starts a Factory Reset confirmation challenge. Performs no mutation; the
///  destructive wipe happens only through `ConfirmReset` once the displayed
///  code is confirmed.
RE::BSFixedString Reset(RE::StaticFunctionTag *) {
  return SendNoArgument(ipc::TrustAdminOperation::kReset);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.ConfirmReset(String)`
///  function: confirms a Factory Reset challenge started by `Reset`,
///  executing the destructive wipe on a matching code.
RE::BSFixedString ConfirmReset(RE::StaticFunctionTag *,
                               RE::BSFixedString akCode) {
  if (!g_session) {
    return RE::BSFixedString(kUnavailableMessage);
  }
  std::string_view confirmationCode(akCode);
  if (!IsFixedAsciiDigits(confirmationCode,
                          ipc::kFactoryResetChallengeCodeDigits)) {
    return RE::BSFixedString(kInvalidConfirmationCodeMessage);
  }
  try {
    return FormatResult(g_session->SendTrustAdminRequest(
        ipc::TrustAdminOperation::kConfirmReset, std::nullopt, std::nullopt,
        std::string(confirmationCode)));
  } catch (...) {
    return RE::BSFixedString(kInternalErrorMessage);
  }
}

///  Binds the nine native functions above to their Papyrus declarations.
bool RegisterFunctions(RE::BSScript::IVirtualMachine *vm) {
  vm->RegisterFunction("List", "DovahLinkAdmin", List);
  vm->RegisterFunction("Help", "DovahLinkAdmin", Help);
  vm->RegisterFunction("Revoke", "DovahLinkAdmin", Revoke);
  vm->RegisterFunction("Block", "DovahLinkAdmin", Block);
  vm->RegisterFunction("Unblock", "DovahLinkAdmin", Unblock);
  vm->RegisterFunction("Forget", "DovahLinkAdmin", Forget);
  vm->RegisterFunction("ResetTrust", "DovahLinkAdmin", ResetTrust);
  vm->RegisterFunction("Reset", "DovahLinkAdmin", Reset);
  vm->RegisterFunction("ConfirmReset", "DovahLinkAdmin", ConfirmReset);
  return true;
}

} //  namespace

void InstallAdapterTrustAdminPapyrusAdapter(ipc::IAdapterIpcSession &session) {
  g_session = &session;

  //  Unlike the plugin's messaging/serialization interfaces, this one backs a
  //  purely optional Skyrim-facing console surface: its absence disables
  //  only this feature, never the rest of the adapter, so this logs and
  //  returns rather than failing plugin load.
  auto *papyrusInterface = SKSE::GetPapyrusInterface();
  if (!papyrusInterface) {
    SKSE::log::warn("SKSE's Papyrus interface is unavailable; the "
                    "trust-administration console adapter will not be "
                    "registered.");
    return;
  }
  if (!papyrusInterface->Register(RegisterFunctions)) {
    SKSE::log::error("DovahLink trust-admin Papyrus function registration "
                     "failed; console commands will remain unavailable.");
  }
}

} //  namespace dovahlink::adapter::papyrus
