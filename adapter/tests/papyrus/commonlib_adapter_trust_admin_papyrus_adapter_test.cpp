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

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter registers every "
          "DovahLinkAdmin function and handles a missing or failed Papyrus "
          "interface",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
  std::string source = Source();

  for (const char *functionName :
       {"List", "Help", "Revoke", "Block", "Unblock", "Forget", "ResetTrust",
        "Reset", "ConfirmReset"}) {
    INFO("checking registration of " << functionName);
    std::string expected = std::string("vm->RegisterFunction(\"") +
                           functionName + "\", \"DovahLinkAdmin\", " +
                           functionName + ");";
    CHECK(source.find(expected) != std::string::npos);
  }

  CHECK(source.find("if (!papyrusInterface)") != std::string::npos);
  CHECK(source.find("if (!papyrusInterface->Register(RegisterFunctions))") !=
        std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter declares all nine "
          "native Papyrus function signatures",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
  std::string source = Source();

  CHECK(source.find("RE::BSFixedString List(RE::StaticFunctionTag *, "
                    "RE::BSFixedString akScope) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString Help(RE::StaticFunctionTag *) {") !=
        std::string::npos);
  CHECK(source.find("RE::BSFixedString Revoke(RE::StaticFunctionTag *, "
                    "RE::BSFixedString akId) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString Block(RE::StaticFunctionTag *, "
                    "RE::BSFixedString akId) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString Unblock(RE::StaticFunctionTag *, "
                    "RE::BSFixedString akId) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString Forget(RE::StaticFunctionTag *, "
                    "RE::BSFixedString akId) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString ResetTrust(RE::StaticFunctionTag "
                    "*) {") != std::string::npos);
  CHECK(source.find("RE::BSFixedString Reset(RE::StaticFunctionTag *) {") !=
        std::string::npos);
  CHECK(source.find("RE::BSFixedString ConfirmReset(RE::StaticFunctionTag "
                    "*,") != std::string::npos);
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

  for (const auto &[operationValue, expectedCall] :
       {std::pair{"kRevoke",
                  "return SendWithShortId(ipc::TrustAdminOperation::kRevoke, "
                  "akId);"},
        std::pair{"kBlock",
                  "return SendWithShortId(ipc::TrustAdminOperation::kBlock, "
                  "akId);"},
        std::pair{"kUnblock",
                  "return SendWithShortId(ipc::TrustAdminOperation::kUnblock,"
                  " akId);"},
        std::pair{"kForget",
                  "return SendWithShortId(ipc::TrustAdminOperation::kForget, "
                  "akId);"}}) {
    INFO("checking " << operationValue);
    CHECK(source.find(expectedCall) != std::string::npos);
  }
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's no-argument functions "
          "each forward their own matching TrustAdminOperation",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
  std::string source = Source();

  CHECK(source.find("return SendNoArgument(ipc::TrustAdminOperation::kHelp)"
                    ";") != std::string::npos);
  CHECK(source.find(
            "return SendNoArgument(ipc::TrustAdminOperation::kResetTrust);") !=
        std::string::npos);
  CHECK(source.find("return SendNoArgument(ipc::TrustAdminOperation::kReset)"
                    ";") != std::string::npos);
}

TEST_CASE("CommonLibAdapterTrustAdminPapyrusAdapter's List validates its "
          "scope before it ever reaches SendTrustAdminRequest",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
  std::string source = Source();

  std::size_t listStart = source.find(
      "RE::BSFixedString List(RE::StaticFunctionTag *, RE::BSFixedString "
      "akScope) {");
  REQUIRE(listStart != std::string::npos);
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
      source.find("RE::BSFixedString ConfirmReset(RE::StaticFunctionTag *,");
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
          "the caller's operation",
          "[papyrus][commonlib_adapter_trust_admin_papyrus_adapter]"
          "[structural]") {
  //  SendNoArgument and SendWithShortId are each called from more than one
  //  Papyrus function; this proves the shared implementation itself -- once
  //  -- rather than repeating the same proof at every call site.
  std::string source = Source();

  std::size_t sendWithShortIdStart = source.find(
      "RE::BSFixedString SendWithShortId(ipc::TrustAdminOperation operation,");
  REQUIRE(sendWithShortIdStart != std::string::npos);
  std::size_t digitsCheck =
      source.find("!IsFixedAsciiDigits(shortId,", sendWithShortIdStart);
  std::size_t sendCall = source.find(
      "operation, std::nullopt, std::string(shortId)", sendWithShortIdStart);

  REQUIRE(digitsCheck != std::string::npos);
  REQUIRE(sendCall != std::string::npos);
  CHECK(digitsCheck < sendCall);

  std::size_t sendNoArgumentStart = source.find(
      "RE::BSFixedString SendNoArgument(ipc::TrustAdminOperation operation) "
      "{");
  REQUIRE(sendNoArgumentStart != std::string::npos);
  CHECK(source.find("SendTrustAdminRequest(operation)", sendNoArgumentStart) !=
        std::string::npos);
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

  CHECK(source.find("scope == \"all\"") != std::string::npos);
  CHECK(source.find("scope == \"trust\"") != std::string::npos);
  CHECK(source.find("scope == \"block\"") != std::string::npos);
  CHECK(source.find("\"Unrecognized list scope.\"") != std::string::npos);
}
