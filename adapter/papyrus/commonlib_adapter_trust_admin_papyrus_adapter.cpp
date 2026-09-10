#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"

#include "papyrus/commonlib_adapter_trust_admin_papyrus_adapter.hpp"

#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_enums.hpp"
#include "runtime/game_thread_completion.hpp"

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
ipc::IAdapterIpcSession* g_session = nullptr;

///  Non-owning pointer to the game-thread marshaller every latent function's
///  terminal `ReturnLatentResult` call is scheduled through, set once by
///  `InstallAdapterTrustAdminPapyrusAdapter` at the same time as `g_session`
///  -- same lifetime and same plain-function-pointer idiom, for the same
///  reason.
runtime::IAdapterTaskMarshaller* g_marshaller = nullptr;

///  Result returned when a native function runs before
///  `InstallAdapterTrustAdminPapyrusAdapter` set the session pointer --
///  unreachable once the plugin has finished loading, kept only so a caller
///  never gets a null-dereference instead of a message.
constexpr const char* kUnavailableMessage =
    "DovahLink trust admin is unavailable.";

///  Result returned when `SendTrustAdminRequest` reports
///  `ipc::TrustAdminRequestOutcome::kUnavailable`: no authenticated host
///  connection was available, the outstanding-request bound was already
///  reached, or the request could not be sent. The host's own state is
///  provably unaffected.
constexpr const char* kHostUnavailableMessage = "host not ready";

///  Result returned when `SendTrustAdminRequest` reports
///  `ipc::TrustAdminRequestOutcome::kTimedOut` for a read-only command
///  (`Help`, `List`, or `Reset`, which only starts a confirmation challenge
///  and mutates no persisted trust state): no correlated result arrived
///  within the bound, but nothing about that is ambiguous since the command
///  itself could not have changed persisted state.
constexpr const char* kRequestTimedOutMessage =
    "The host did not respond in time.";

///  Result returned when `SendTrustAdminRequest` reports
///  `ipc::TrustAdminRequestOutcome::kTimedOut` for a command that durably
///  mutates trust state (`Revoke`, `Block`, `Unblock`, `Forget`,
///  `ResetTrust`, or `ConfirmReset`): the host's own mutation may have
///  already committed even though no correlated result arrived in time, so
///  this must never be reported as a plain, unambiguous failure.
constexpr const char* kMutatingRequestTimedOutMessage =
    "The host did not respond in time. The operation may have completed; "
    "check the current trust state before retrying.";

///  Result returned when a native function throws.
///  `ai/context/skse/cpp-style.md`: "never allow an exception to escape a
///  callback" -- a native Papyrus function is called directly by the game's
///  Papyrus VM, not through this codebase's own containment, so it is
///  exactly such a boundary.
constexpr const char* kInternalErrorMessage =
    "DovahLink trust admin failed unexpectedly.";

///  Result returned when `List`'s scope argument is not one of "all",
///  "trust", or "block".
constexpr const char* kUnrecognizedScopeMessage = "Unrecognized list scope.";

///  Result returned when a short-id-targeted command's argument is not
///  exactly `ipc::kPairingShortIdDigits` ASCII decimal digits.
constexpr const char* kInvalidShortIdMessage = "Invalid device id.";

///  Result returned when `ConfirmReset`'s argument is not exactly
///  `ipc::kFactoryResetChallengeCodeDigits` ASCII decimal digits.
constexpr const char* kInvalidConfirmationCodeMessage =
    "Invalid confirmation code.";

///  Whether every character in `text` is an ASCII decimal digit and `text`
///  is exactly `expectedLength` characters long.
bool IsFixedAsciiDigits(std::string_view text, std::size_t expectedLength) {
    return text.size() == expectedLength &&
           std::ranges::all_of(text, [](char c) { return c >= '0' && c <= '9'; });
}

///  Parses `List`'s scope argument into its closed wire value. An empty scope -- ConsoleUtil
///  Extended's own representation of an omitted optional argument -- is treated the same as
///  "all".
std::optional<ipc::TrustAdminListScope> ParseListScope(std::string_view scope) {
    if (scope.empty() || scope == "all") {
        return ipc::TrustAdminListScope::kAll;
    }
    if (scope == "trusted") {
        return ipc::TrustAdminListScope::kTrust;
    }
    if (scope == "blocked") {
        return ipc::TrustAdminListScope::kBlock;
    }
    return std::nullopt;
}

///  Whether `operation` durably mutates persisted trust state, so a timeout
///  after it was submitted must be reported with
///  `kMutatingRequestTimedOutMessage` rather than the plain
///  `kRequestTimedOutMessage`: the host's own mutation may have already
///  committed even though no correlated result arrived in time. `kReset` only
///  starts a Factory Reset confirmation challenge -- the destructive wipe
///  happens only through a separate `kConfirmReset` call -- so it is not
///  mutating for this purpose.
bool IsMutatingTrustAdminOperation(ipc::TrustAdminOperation operation) {
    switch (operation) {
    case ipc::TrustAdminOperation::kRevoke:
    case ipc::TrustAdminOperation::kBlock:
    case ipc::TrustAdminOperation::kUnblock:
    case ipc::TrustAdminOperation::kForget:
    case ipc::TrustAdminOperation::kResetTrust:
    case ipc::TrustAdminOperation::kConfirmReset:
        return true;
    case ipc::TrustAdminOperation::kHelp:
    case ipc::TrustAdminOperation::kList:
    case ipc::TrustAdminOperation::kReset:
        return false;
    }
    return false;
}

///  Formats a `SendTrustAdminRequest` result for `operation`: the host's
///  result text on `kCompleted`, the controlled unavailable message on
///  `kUnavailable`, or a timeout message on `kTimedOut` -- worded plainly for
///  a read-only `operation` and worded to disclose the ambiguous final state
///  for one that durably mutates trust state, per
///  `IsMutatingTrustAdminOperation`.
RE::BSFixedString FormatResult(ipc::TrustAdminRequestResult result,
                               ipc::TrustAdminOperation operation) {
    switch (result.outcome) {
    case ipc::TrustAdminRequestOutcome::kCompleted:
        return RE::BSFixedString(result.resultText->c_str());
    case ipc::TrustAdminRequestOutcome::kUnavailable:
        return RE::BSFixedString(kHostUnavailableMessage);
    case ipc::TrustAdminRequestOutcome::kTimedOut:
        return RE::BSFixedString(IsMutatingTrustAdminOperation(operation)
                                     ? kMutatingRequestTimedOutMessage
                                     : kRequestTimedOutMessage);
    }
    return RE::BSFixedString(kInternalErrorMessage);
}

///  Resumes the Papyrus stack `a_stackID` suspended on with `message`. Must
///  run on the game thread -- the one thread Skyrim's scripting VM supports
///  -- per `RE::BSScript::IVirtualMachine::ReturnLatentResult`'s own
///  contract; called only from the task `RespondLatentOnGameThread` schedules
///  there, never directly.
void RespondLatent(RE::BSScript::Internal::VirtualMachine* a_vm,
                   RE::VMStackID a_stackID, RE::BSFixedString message) {
    a_vm->ReturnLatentResult(a_stackID, message);
}

///  Schedules `RespondLatent(a_vm, a_stackID, message)` onto the game thread
///  through `g_marshaller`. The single call site every latent trust-admin
///  function's eventual response -- whether an immediate controlled
///  rejection this adapter decides locally or `SendTrustAdminRequest`'s
///  asynchronously delivered result -- goes through, so every response path
///  is proven to actually unblock its script exactly once, on the one
///  thread that supports it, regardless of which thread reaches this call:
///  the thread the Papyrus VM invoked the originating native function on
///  must not be assumed to already be the game thread. If `g_marshaller`
///  itself fails to accept the task, the script remains suspended and the
///  failure is logged; see `runtime::RunOnGameThreadOrReportFailure`'s own
///  documentation for why running `ReturnLatentResult` here, on the wrong
///  thread, as a fallback is not an acceptable alternative. A null
///  `g_marshaller` -- unreachable once the plugin has finished loading, the
///  same guarantee `g_session`'s own null check documents, since both are
///  set together, before `RegisterFunctions` is ever registered with the
///  Papyrus VM -- is handled the same defensive way: logged, script left
///  suspended, rather than a null-dereference.
void RespondLatentOnGameThread(RE::BSScript::Internal::VirtualMachine* a_vm,
                               RE::VMStackID a_stackID,
                               RE::BSFixedString message) {
    if (!g_marshaller) {
        SKSE::log::error("DovahLink trust-admin: the game-thread marshaller is "
                         "unavailable; a latent script's terminal result cannot "
                         "be delivered.");
        return;
    }
    runtime::RunOnGameThreadOrReportFailure(
        *g_marshaller,
        [a_vm, a_stackID, message] { RespondLatent(a_vm, a_stackID, message); },
        [] {
            SKSE::log::error("DovahLink trust-admin: the game-thread scheduler "
                             "rejected a latent script's terminal result; the "
                             "script remains suspended.");
        });
}

///  Sends one no-argument trust-admin request and resumes the calling script
///  with its formatted result. Shared by `Help`, `ResetTrust`, and `Reset`:
///  each is itself the framework-mandated plain-function signature SKSE's
///  latent Papyrus binding requires, and this ordinary helper reaches
///  plugin-lifetime state through the same file-local `g_session` pointer
///  those functions already do, rather than accepting it as a parameter.
///  Never blocks the calling thread: `SendTrustAdminRequest` always resolves
///  its callback asynchronously (or immediately, but never by waiting), so
///  every path here returns `kStarted` having, at most, only enqueued work.
RE::BSScript::LatentStatus
SendNoArgument(RE::BSScript::Internal::VirtualMachine* a_vm,
               RE::VMStackID a_stackID, ipc::TrustAdminOperation operation) {
    if (!g_session) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kUnavailableMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    try {
        g_session->SendTrustAdminRequest(
            operation, std::nullopt, std::nullopt, std::nullopt,
            [a_vm, a_stackID, operation](ipc::TrustAdminRequestResult result) {
                RespondLatentOnGameThread(a_vm, a_stackID,
                                          FormatResult(std::move(result), operation));
            });
    } catch (...) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kInternalErrorMessage));
    }
    return RE::BSScript::LatentStatus::kStarted;
}

///  Sends one short-id-targeted trust-admin request and resumes the calling
///  script with its formatted result, rejecting a malformed id before it
///  ever reaches the host. Never blocks the calling thread; see
///  `SendNoArgument`.
RE::BSScript::LatentStatus
SendWithShortId(RE::BSScript::Internal::VirtualMachine* a_vm,
                RE::VMStackID a_stackID, ipc::TrustAdminOperation operation,
                RE::BSFixedString akId) {
    if (!g_session) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kUnavailableMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    std::string_view shortId(akId);
    if (!IsFixedAsciiDigits(shortId, ipc::kPairingShortIdDigits)) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kInvalidShortIdMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    try {
        g_session->SendTrustAdminRequest(
            operation, std::nullopt, std::string(shortId), std::nullopt,
            [a_vm, a_stackID, operation](ipc::TrustAdminRequestResult result) {
                RespondLatentOnGameThread(a_vm, a_stackID,
                                          FormatResult(std::move(result), operation));
            });
    } catch (...) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kInternalErrorMessage));
    }
    return RE::BSScript::LatentStatus::kStarted;
}

///  Native implementation of the Papyrus `DovahLinkAdmin.List(String)`
///  function. Latent: never blocks the calling thread, including the
///  thread SKSE invokes this initial callback on; see `SendNoArgument`.
RE::BSScript::LatentStatus List(RE::BSScript::Internal::VirtualMachine* a_vm,
                                RE::VMStackID a_stackID,
                                RE::StaticFunctionTag*,
                                RE::BSFixedString akScope) {
    if (!g_session) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kUnavailableMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    std::optional<ipc::TrustAdminListScope> scope =
        ParseListScope(std::string_view(akScope));
    if (!scope.has_value()) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kUnrecognizedScopeMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    try {
        g_session->SendTrustAdminRequest(
            ipc::TrustAdminOperation::kList, *scope, std::nullopt, std::nullopt,
            [a_vm, a_stackID](ipc::TrustAdminRequestResult result) {
                RespondLatentOnGameThread(
                    a_vm, a_stackID,
                    FormatResult(std::move(result), ipc::TrustAdminOperation::kList));
            });
    } catch (...) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kInternalErrorMessage));
    }
    return RE::BSScript::LatentStatus::kStarted;
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Help()` function.
RE::BSScript::LatentStatus Help(RE::BSScript::Internal::VirtualMachine* a_vm,
                                RE::VMStackID a_stackID,
                                RE::StaticFunctionTag*) {
    return SendNoArgument(a_vm, a_stackID, ipc::TrustAdminOperation::kHelp);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Revoke(String)`
///  function.
RE::BSScript::LatentStatus Revoke(RE::BSScript::Internal::VirtualMachine* a_vm,
                                  RE::VMStackID a_stackID,
                                  RE::StaticFunctionTag*,
                                  RE::BSFixedString akId) {
    return SendWithShortId(a_vm, a_stackID, ipc::TrustAdminOperation::kRevoke,
                           akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Block(String)`
///  function.
RE::BSScript::LatentStatus Block(RE::BSScript::Internal::VirtualMachine* a_vm,
                                 RE::VMStackID a_stackID,
                                 RE::StaticFunctionTag*,
                                 RE::BSFixedString akId) {
    return SendWithShortId(a_vm, a_stackID, ipc::TrustAdminOperation::kBlock,
                           akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Unblock(String)`
///  function.
RE::BSScript::LatentStatus Unblock(RE::BSScript::Internal::VirtualMachine* a_vm,
                                   RE::VMStackID a_stackID,
                                   RE::StaticFunctionTag*,
                                   RE::BSFixedString akId) {
    return SendWithShortId(a_vm, a_stackID, ipc::TrustAdminOperation::kUnblock,
                           akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Forget(String)`
///  function.
RE::BSScript::LatentStatus Forget(RE::BSScript::Internal::VirtualMachine* a_vm,
                                  RE::VMStackID a_stackID,
                                  RE::StaticFunctionTag*,
                                  RE::BSFixedString akId) {
    return SendWithShortId(a_vm, a_stackID, ipc::TrustAdminOperation::kForget,
                           akId);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.ResetTrust()`
///  function: the recoverable, non-destructive bulk revoke -- unlike
///  `Reset`/`ConfirmReset`, requires no confirmation code.
RE::BSScript::LatentStatus
ResetTrust(RE::BSScript::Internal::VirtualMachine* a_vm,
           RE::VMStackID a_stackID, RE::StaticFunctionTag*) {
    return SendNoArgument(a_vm, a_stackID, ipc::TrustAdminOperation::kResetTrust);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.Reset()` function:
///  starts a Factory Reset confirmation challenge. Performs no mutation; the
///  destructive wipe happens only through `ConfirmReset` once the displayed
///  code is confirmed.
RE::BSScript::LatentStatus Reset(RE::BSScript::Internal::VirtualMachine* a_vm,
                                 RE::VMStackID a_stackID,
                                 RE::StaticFunctionTag*) {
    return SendNoArgument(a_vm, a_stackID, ipc::TrustAdminOperation::kReset);
}

///  Native implementation of the Papyrus `DovahLinkAdmin.ConfirmReset(String)`
///  function: confirms a Factory Reset challenge started by `Reset`,
///  executing the destructive wipe on a matching code.
RE::BSScript::LatentStatus
ConfirmReset(RE::BSScript::Internal::VirtualMachine* a_vm,
             RE::VMStackID a_stackID, RE::StaticFunctionTag*,
             RE::BSFixedString akCode) {
    if (!g_session) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kUnavailableMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    std::string_view confirmationCode(akCode);
    if (!IsFixedAsciiDigits(confirmationCode,
                            ipc::kFactoryResetChallengeCodeDigits)) {
        RespondLatentOnGameThread(
            a_vm, a_stackID, RE::BSFixedString(kInvalidConfirmationCodeMessage));
        return RE::BSScript::LatentStatus::kStarted;
    }
    try {
        g_session->SendTrustAdminRequest(
            ipc::TrustAdminOperation::kConfirmReset, std::nullopt, std::nullopt,
            std::string(confirmationCode),
            [a_vm, a_stackID](ipc::TrustAdminRequestResult result) {
                RespondLatentOnGameThread(
                    a_vm, a_stackID,
                    FormatResult(std::move(result),
                                 ipc::TrustAdminOperation::kConfirmReset));
            });
    } catch (...) {
        RespondLatentOnGameThread(a_vm, a_stackID,
                                  RE::BSFixedString(kInternalErrorMessage));
    }
    return RE::BSScript::LatentStatus::kStarted;
}

///  Binds the nine native functions above to their Papyrus declarations, as
///  latent functions: none of them ever blocks the thread the Papyrus VM
///  calls its initial callback on, per this concept's "no blocking wait on a
///  game-thread callback" requirement -- each one enqueues work and resumes
///  the calling script later via
///  `RE::BSScript::IVirtualMachine::ReturnLatentResult`.
bool RegisterFunctions(RE::BSScript::IVirtualMachine* vm) {
    vm->RegisterLatentFunction<RE::BSFixedString>("List", "DovahLinkAdmin", List);
    vm->RegisterLatentFunction<RE::BSFixedString>("Help", "DovahLinkAdmin", Help);
    vm->RegisterLatentFunction<RE::BSFixedString>("Revoke", "DovahLinkAdmin",
                                                  Revoke);
    vm->RegisterLatentFunction<RE::BSFixedString>("Block", "DovahLinkAdmin",
                                                  Block);
    vm->RegisterLatentFunction<RE::BSFixedString>("Unblock", "DovahLinkAdmin",
                                                  Unblock);
    vm->RegisterLatentFunction<RE::BSFixedString>("Forget", "DovahLinkAdmin",
                                                  Forget);
    vm->RegisterLatentFunction<RE::BSFixedString>("ResetTrust", "DovahLinkAdmin",
                                                  ResetTrust);
    vm->RegisterLatentFunction<RE::BSFixedString>("Reset", "DovahLinkAdmin",
                                                  Reset);
    vm->RegisterLatentFunction<RE::BSFixedString>("ConfirmReset",
                                                  "DovahLinkAdmin", ConfirmReset);
    return true;
}

} //  namespace

void InstallAdapterTrustAdminPapyrusAdapter(
    ipc::IAdapterIpcSession& session,
    runtime::IAdapterTaskMarshaller& marshaller) {
    g_session = &session;
    g_marshaller = &marshaller;

    //  Unlike the plugin's messaging/serialization interfaces, this one backs a
    //  purely optional Skyrim-facing console surface: its absence disables
    //  only this feature, never the rest of the adapter, so this logs and
    //  returns rather than failing plugin load.
    auto* papyrusInterface = SKSE::GetPapyrusInterface();
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
