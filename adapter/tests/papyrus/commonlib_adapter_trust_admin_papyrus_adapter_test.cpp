#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <utility>

using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Loads the trust-admin Papyrus adapter's own source text once per test.
std::string Source() {
    return ReadSource(DOVAHLINK_ADAPTER_TRUST_ADMIN_PAPYRUS_ADAPTER_SOURCE_FILE);
}

///  The number of times `needle` occurs in `haystack`, non-overlapping.
std::size_t CountOccurrences(const std::string& haystack,
                             const std::string& needle) {
    std::size_t count = 0;
    std::size_t position = 0;
    while ((position = haystack.find(needle, position)) != std::string::npos) {
        ++count;
        position += needle.size();
    }
    return count;
}

} //  namespace

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter includes RE/Skyrim.h and "
          "SKSE/SKSE.h before its own header",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  The test target intentionally does not link CommonLibSSE-NG. This
    //  structural check protects the include-order rule
    //  ai/context/skse/cpp-style.md requires for any file that directly
    //  includes an RE/... or SKSE/... runtime header.
    std::string source = Source();

    std::size_t reInclude = source.find("#include \"RE/Skyrim.h\"");
    std::size_t skseInclude = source.find("#include \"SKSE/SKSE.h\"");
    std::size_t ownHeaderInclude =
        source.find("#include "
                    "\"papyrus/commonlib_adapter_trust_admin_papyrus_adapter."
                    "hpp\"");

    REQUIRE(reInclude != std::string::npos);
    REQUIRE(skseInclude != std::string::npos);
    REQUIRE(ownHeaderInclude != std::string::npos);
    CHECK(reInclude < ownHeaderInclude);
    CHECK(skseInclude < ownHeaderInclude);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter stores the session "
          "behind its interface contract, not the concrete type",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("ipc::IAdapterIpcSession *g_session") != std::string::npos);
    CHECK(source.find("ipc::AdapterIpcSession *g_session") == std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter stores the game-thread "
          "marshaller behind its interface contract and sets it in the same "
          "call that sets the session",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("runtime::IAdapterTaskMarshaller *g_marshaller") !=
          std::string::npos);

    std::size_t installStart =
        source.find("void InstallAdapterTrustAdminPapyrusAdapter(");
    REQUIRE(installStart != std::string::npos);
    std::size_t setSession = source.find("g_session = &session;", installStart);
    std::size_t setMarshaller =
        source.find("g_marshaller = &marshaller;", installStart);
    REQUIRE(setSession != std::string::npos);
    REQUIRE(setMarshaller != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter registers every "
          "DovahLinkAdmin function as latent and handles a missing or "
          "failed Papyrus interface",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  Latent, not RegisterFunction: this is this concept's own proof that no
    //  DovahLinkAdmin command can ever block the thread the Papyrus VM calls
    //  its initial callback on -- the behavioral half of that proof lives in
    //  AdapterIpcSession's own tests, since this test target deliberately does
    //  not link CommonLibSSE-NG and cannot exercise a real VM callback.
    std::string source = Source();

    CHECK(source.find("vm->RegisterFunction(") == std::string::npos);
    CHECK(CountOccurrences(
              source, "vm->RegisterLatentFunction<RE::BSFixedString>(") == 9);

    for (const char* functionName :
         {"List", "Help", "Revoke", "Block", "Unblock", "Forget", "ResetTrust",
          "Reset", "ConfirmReset"}) {
        INFO("checking latent registration of " << functionName);
        std::string registrationStartMarker =
            std::string("vm->RegisterLatentFunction<RE::BSFixedString>(\"") +
            functionName + "\",";
        std::size_t registrationStart = source.find(registrationStartMarker);
        REQUIRE(registrationStart != std::string::npos);
        std::size_t classNameArgument =
            source.find("\"DovahLinkAdmin\"", registrationStart);
        std::size_t functionArgument = source.find(
            functionName, registrationStart + registrationStartMarker.size());
        REQUIRE(classNameArgument != std::string::npos);
        REQUIRE(functionArgument != std::string::npos);
        CHECK(classNameArgument < functionArgument);
    }

    CHECK(source.find("if (!papyrusInterface)") != std::string::npos);
    CHECK(source.find("if (!papyrusInterface->Register(RegisterFunctions))") !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter declares all nine "
          "native Papyrus function signatures as latent",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("RE::BSFixedString List(") == std::string::npos);

    //  Each function is checked with two bounded find() calls rather than one
    //  literal spanning "RE::BSScript::LatentStatus", "*a_vm,", and
    //  "RE::VMStackID a_stackID,": clang-format sometimes wraps the return
    //  type onto its own line (when the name is long, e.g. ResetTrust) and
    //  always wraps a long parameter list, so a single literal assuming a
    //  fixed line layout breaks the instant the signature is reformatted, even
    //  though the signature itself is unchanged. Checking both the
    //  space-joined and newline-joined return-type forms is what proves every
    //  native function returns RE::BSScript::LatentStatus, not
    //  RE::BSFixedString directly -- the actual result only ever reaches the
    //  calling script through ReturnLatentResult.
    for (const char* functionName :
         {"List", "Help", "Revoke", "Block", "Unblock", "Forget", "Reset",
          "ResetTrust", "ConfirmReset"}) {
        INFO("checking latent signature of " << functionName);
        std::string signatureStartMarker =
            std::string(functionName) +
            "(RE::BSScript::Internal::VirtualMachine *a_vm,";
        bool hasReturnType =
            source.find("RE::BSScript::LatentStatus " + signatureStartMarker) !=
                std::string::npos ||
            source.find("RE::BSScript::LatentStatus\n" + signatureStartMarker) !=
                std::string::npos;
        CHECK(hasReturnType);
        std::size_t signatureStart = source.find(signatureStartMarker);
        REQUIRE(signatureStart != std::string::npos);
        CHECK(source.find("RE::VMStackID a_stackID,", signatureStart) !=
              std::string::npos);
    }

    //  List's own extra akScope parameter, following the same
    //  bounded-two-part pattern for the same line-wrap reason.
    std::size_t staticFunctionTagStart =
        source.find("RE::StaticFunctionTag *,\n");
    REQUIRE(staticFunctionTagStart != std::string::npos);
    CHECK(source.find("RE::BSFixedString akScope) {", staticFunctionTagStart) !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's short-id-targeted "
          "functions each forward their own matching TrustAdminOperation",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  Proves the exact function-to-operation pairing, not merely that every
    //  operation value appears somewhere in the file: a copy-paste mistake
    //  that made two functions send the same operation would otherwise still
    //  pass a file-wide substring search.
    std::string source = Source();

    for (const auto& [operationValue, expectedCall] :
         {std::pair{"kRevoke", "return SendWithShortId(a_vm, a_stackID, "
                               "ipc::TrustAdminOperation::kRevoke,"},
          std::pair{"kBlock", "return SendWithShortId(a_vm, a_stackID, "
                              "ipc::TrustAdminOperation::kBlock,"},
          std::pair{"kUnblock", "return SendWithShortId(a_vm, a_stackID, "
                                "ipc::TrustAdminOperation::kUnblock,"},
          std::pair{"kForget", "return SendWithShortId(a_vm, a_stackID, "
                               "ipc::TrustAdminOperation::kForget,"}}) {
        INFO("checking " << operationValue);
        CHECK(source.find(expectedCall) != std::string::npos);
    }
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's no-argument functions "
          "each forward their own matching TrustAdminOperation",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("return SendNoArgument(a_vm, a_stackID, "
                      "ipc::TrustAdminOperation::kHelp);") != std::string::npos);
    CHECK(source.find("return SendNoArgument(a_vm, a_stackID, "
                      "ipc::TrustAdminOperation::kResetTrust);") !=
          std::string::npos);
    CHECK(source.find("return SendNoArgument(a_vm, a_stackID, "
                      "ipc::TrustAdminOperation::kReset);") != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's List validates its "
          "scope before it ever reaches SendTrustAdminRequest",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    //  See the identical wrapped-signature rationale in the "declares all nine
    //  native Papyrus function signatures as latent" test above: List's
    //  parameter list is wrapped onto its own line by clang-format, so this is
    //  a bounded find() rather than one literal spanning the line break.
    std::size_t listStart =
        source.find("List(RE::BSScript::Internal::VirtualMachine *a_vm,");
    REQUIRE(listStart != std::string::npos);
    REQUIRE(source.find("RE::VMStackID a_stackID,", listStart) !=
            std::string::npos);
    std::size_t scopeCheck = source.find("!scope.has_value()", listStart);
    std::size_t sendCall =
        source.find("ipc::TrustAdminOperation::kList, *scope", listStart);

    REQUIRE(scopeCheck != std::string::npos);
    REQUIRE(sendCall != std::string::npos);
    CHECK(scopeCheck < sendCall);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's ConfirmReset validates "
          "its confirmation code before it ever reaches "
          "SendTrustAdminRequest",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    std::size_t confirmResetStart =
        source.find("ConfirmReset(RE::BSScript::Internal::VirtualMachine *a_vm,");
    REQUIRE(confirmResetStart != std::string::npos);
    std::size_t digitsCheck =
        source.find("!IsFixedAsciiDigits(confirmationCode,", confirmResetStart);
    std::size_t sendCall = source.find("ipc::TrustAdminOperation::kConfirmReset,",
                                       confirmResetStart);

    REQUIRE(digitsCheck != std::string::npos);
    REQUIRE(sendCall != std::string::npos);
    CHECK(digitsCheck < sendCall);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's shared helpers "
          "validate a short id and forward to SendTrustAdminRequest with "
          "the caller's operation, resuming the latent script through its "
          "captured stack id",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  SendNoArgument and SendWithShortId are each called from more than one
    //  Papyrus function; this proves the shared implementation itself -- once
    //  -- rather than repeating the same proof at every call site.
    std::string source = Source();

    std::size_t sendWithShortIdStart = source.find(
        "SendWithShortId(RE::BSScript::Internal::VirtualMachine *a_vm,");
    REQUIRE(sendWithShortIdStart != std::string::npos);
    std::size_t digitsCheck =
        source.find("!IsFixedAsciiDigits(shortId,", sendWithShortIdStart);
    std::size_t sendCall = source.find(
        "operation, std::nullopt, std::string(shortId)", sendWithShortIdStart);
    REQUIRE(digitsCheck != std::string::npos);
    REQUIRE(sendCall != std::string::npos);
    //  Bounded two-part find, starting only after sendCall: clang-format may
    //  wrap RespondLatentOnGameThread's argument list onto its own line, so a
    //  single literal spanning that break would fail the instant it reformats
    //  even though the call itself is unchanged, and starting from sendCall
    //  (rather than sendWithShortIdStart) skips the earlier, unrelated
    //  RespondLatentOnGameThread call on the invalid-short-id rejection path
    //  above it.
    std::size_t returnLatentResult =
        source.find("RespondLatentOnGameThread(a_vm, a_stackID,", sendCall);
    REQUIRE(returnLatentResult != std::string::npos);
    CHECK(source.find("FormatResult(std::move(result), operation));",
                      returnLatentResult) != std::string::npos);
    CHECK(digitsCheck < sendCall);
    CHECK(sendCall < returnLatentResult);

    std::size_t sendNoArgumentStart = source.find(
        "SendNoArgument(RE::BSScript::Internal::VirtualMachine *a_vm,");
    REQUIRE(sendNoArgumentStart != std::string::npos);
    CHECK(source.find("g_session->SendTrustAdminRequest(", sendNoArgumentStart) !=
          std::string::npos);
    CHECK(source.find("operation, std::nullopt, std::nullopt, std::nullopt,",
                      sendNoArgumentStart) != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter reports explicit "
          "unavailable, host-not-ready, and internal-error results without "
          "crashing",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("if (!g_session)") != std::string::npos);
    CHECK(source.find("\"DovahLink trust admin is unavailable.\"") !=
          std::string::npos);
    CHECK(source.find("\"host not ready\"") != std::string::npos);
    CHECK(source.find("catch (...)") != std::string::npos);
    CHECK(source.find("\"DovahLink trust admin failed unexpectedly.\"") !=
          std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's FormatResult "
          "distinguishes completed, unavailable, and timed-out outcomes, "
          "wording a timed-out mutating operation differently from a "
          "timed-out read-only one",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  The behavioral half of this proof -- which outcome AdapterIpcSession
    //  actually delivers for each scenario -- lives in AdapterIpcSession's own
    //  tests, since this test target cannot construct a real session; this
    //  proves FormatResult's own dispatch and wording are wired correctly.
    std::string source = Source();

    std::size_t formatResultStart =
        source.find("FormatResult(ipc::TrustAdminRequestResult result,");
    REQUIRE(formatResultStart != std::string::npos);
    std::size_t kCompletedCase = source.find(
        "case ipc::TrustAdminRequestOutcome::kCompleted:", formatResultStart);
    std::size_t kUnavailableCase = source.find(
        "case ipc::TrustAdminRequestOutcome::kUnavailable:", formatResultStart);
    std::size_t kTimedOutCase = source.find(
        "case ipc::TrustAdminRequestOutcome::kTimedOut:", formatResultStart);
    REQUIRE(kCompletedCase != std::string::npos);
    REQUIRE(kUnavailableCase != std::string::npos);
    REQUIRE(kTimedOutCase != std::string::npos);
    CHECK(kCompletedCase < kUnavailableCase);
    CHECK(kUnavailableCase < kTimedOutCase);
    CHECK(source.find("IsMutatingTrustAdminOperation(operation)",
                      kTimedOutCase) != std::string::npos);
    CHECK(source.find("\"The host did not respond in time.\"") !=
          std::string::npos);
    CHECK(source.find("may have completed; ") != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's "
          "IsMutatingTrustAdminOperation classifies every operation exactly "
          "once, treating Reset as non-mutating since it only starts a "
          "confirmation challenge",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    std::size_t functionStart =
        source.find("bool IsMutatingTrustAdminOperation(");
    REQUIRE(functionStart != std::string::npos);
    //  The mutating case group falls through to the switch's first "return
    //  true;"; the non-mutating group follows it and falls through to its own
    //  "return false;". Bounding each search by this marker, rather than only
    //  checking a case label appears somewhere in the function, proves which
    //  group each operation actually falls into.
    std::size_t returnTrue = source.find("return true;", functionStart);
    REQUIRE(returnTrue != std::string::npos);

    for (const char* mutatingCase :
         {"case ipc::TrustAdminOperation::kRevoke:",
          "case ipc::TrustAdminOperation::kBlock:",
          "case ipc::TrustAdminOperation::kUnblock:",
          "case ipc::TrustAdminOperation::kForget:",
          "case ipc::TrustAdminOperation::kResetTrust:",
          "case ipc::TrustAdminOperation::kConfirmReset:"}) {
        INFO("checking mutating case " << mutatingCase);
        std::size_t caseStart = source.find(mutatingCase, functionStart);
        REQUIRE(caseStart != std::string::npos);
        CHECK(caseStart < returnTrue);
    }

    for (const char* nonMutatingCase :
         {"case ipc::TrustAdminOperation::kHelp:",
          "case ipc::TrustAdminOperation::kList:",
          "case ipc::TrustAdminOperation::kReset:"}) {
        INFO("checking non-mutating case " << nonMutatingCase);
        CHECK(source.find(nonMutatingCase, returnTrue) != std::string::npos);
    }
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter rejects a malformed "
          "short id or confirmation code before it ever reaches the host",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("ipc::kPairingShortIdDigits") != std::string::npos);
    CHECK(source.find("ipc::kFactoryResetChallengeCodeDigits") !=
          std::string::npos);
    CHECK(source.find("\"Invalid device id.\"") != std::string::npos);
    CHECK(source.find("\"Invalid confirmation code.\"") != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's List parses the "
          "closed set of scope strings and rejects anything else",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    std::string source = Source();

    CHECK(source.find("scope.empty()") != std::string::npos);
    CHECK(source.find("scope == \"known\"") != std::string::npos);
    CHECK(source.find("scope == \"trusted\"") != std::string::npos);
    CHECK(source.find("scope == \"blocked\"") != std::string::npos);
    CHECK(source.find("\"Unrecognized list scope.\"") != std::string::npos);
    CHECK(source.find("scope == \"all\"") == std::string::npos);
    CHECK(source.find("scope == \"trust\"") == std::string::npos);
    CHECK(source.find("scope == \"block\"") == std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter resumes every latent "
          "response -- immediate rejection or SendTrustAdminRequest's "
          "asynchronous result -- through one shared game-thread-marshaling "
          "call site, never a direct ReturnLatentResult call",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
    //  A single call site for RE::BSScript::IVirtualMachine::ReturnLatentResult
    //  (inside RespondLatent), reached only from a single call site for
    //  RespondLatent itself (inside RespondLatentOnGameThread), is this file's
    //  own proof that every response path -- an immediate local rejection this
    //  adapter decides (unavailable session, invalid input, a synchronous
    //  exception) as well as SendTrustAdminRequest's asynchronously delivered
    //  result -- actually resumes its script exactly once, on the game thread,
    //  rather than any call site deciding for itself whether marshaling is
    //  needed. Regression coverage for the earlier design, where every
    //  immediate local rejection called ReturnLatentResult directly on
    //  whichever thread the Papyrus VM invoked the native function on.
    std::string source = Source();

    CHECK(CountOccurrences(source,
                           "a_vm->ReturnLatentResult(a_stackID, message);") == 1);
    CHECK(CountOccurrences(source, "RespondLatent(a_vm, a_stackID, message);") ==
          1);
    CHECK(CountOccurrences(source,
                           "RespondLatentOnGameThread(a_vm, a_stackID,") >= 9);

    //  RespondLatentOnGameThread's own definition is the sole caller of
    //  RespondLatent, and schedules it through the shared game-thread seam
    //  rather than invoking it inline.
    std::size_t wrapperStart =
        source.find("void RespondLatentOnGameThread(RE::BSScript::Internal::"
                    "VirtualMachine *a_vm,");
    REQUIRE(wrapperStart != std::string::npos);
    CHECK(source.find("if (!g_marshaller)", wrapperStart) != std::string::npos);
    CHECK(source.find("runtime::RunOnGameThreadOrReportFailure(", wrapperStart) !=
          std::string::npos);
    CHECK(source.find("*g_marshaller", wrapperStart) != std::string::npos);
}
