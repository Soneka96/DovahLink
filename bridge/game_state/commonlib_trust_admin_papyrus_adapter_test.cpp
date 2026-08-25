#include <catch2/catch_test_macros.hpp>

#include <array>
#include <fstream>
#include <sstream>
#include <string>

namespace {

/// Reads the Papyrus adapter's own source for structural registration
/// assertions.
std::string ReadTrustAdminAdapterSource() {
  std::ifstream file(DOVAHLINK_TRUST_ADMIN_PAPYRUS_SOURCE_FILE);
  REQUIRE(file.is_open());
  std::ostringstream buffer;
  buffer << file.rdbuf();
  return buffer.str();
}

} // namespace

// Papyrus VM registration requires a running Skyrim process, so this test
// protects the adapter surface structurally instead: the canonical List/Help
// functions must be forwarded and the old split listing functions must not be
// registered again.
TEST_CASE(
    "Trust-admin Papyrus registration exposes the canonical listing surface",
    "[game_state][registration]") {
  const std::string source = ReadTrustAdminAdapterSource();

  CHECK(source.find("RE::BSFixedString List(RE::StaticFunctionTag*, "
                    "RE::BSFixedString akScope)") != std::string::npos);
  CHECK(source.find("g_trustAdminService->List(std::string_view(akScope))") !=
        std::string::npos);
  CHECK(source.find("RE::BSFixedString Help(RE::StaticFunctionTag*)") !=
        std::string::npos);
  CHECK(source.find("g_trustAdminService->Help()") != std::string::npos);
  CHECK(source.find(
            "g_trustAdminService->RevokeByShortId(std::string_view(akId))") !=
        std::string::npos);
  CHECK(source.find("g_trustAdminService->BlockByShortId(std::string_view(akId)"
                    ", std::chrono::steady_clock::now())") !=
        std::string::npos);
  CHECK(source.find(
            "g_trustAdminService->UnblockByShortId(std::string_view(akId))") !=
        std::string::npos);
  CHECK(source.find(
            "g_trustAdminService->ForgetByShortId(std::string_view(akId))") !=
        std::string::npos);

  for (const auto &functionName :
       std::array{"List", "Help", "Revoke", "Reset", "Block", "Unblock",
                  "Forget", "ConfirmReset", "ResetTrust"}) {
    const std::string registration =
        "vm->RegisterFunction(\"" + std::string(functionName) +
        "\", \"DovahLinkAdmin\", " + functionName + ");";
    CHECK(source.find(registration) != std::string::npos);
  }
  CHECK(source.find("vm->RegisterFunction(\"Devices\"") == std::string::npos);
  CHECK(source.find("vm->RegisterFunction(\"Blocked\"") == std::string::npos);
  CHECK(source.find("if (!papyrus)") != std::string::npos);
  CHECK(source.find("if (!papyrus->Register(RegisterFunctions))") !=
        std::string::npos);
}

// Reset now only starts the confirmation challenge; the destructive wipe moved
// to ConfirmReset, and Reset Trust's non-destructive bulk revoke is its own
// separate function.
TEST_CASE("Trust-admin Papyrus registration exposes Reset Trust and Factory "
          "Reset confirmation",
          "[game_state][registration]") {
  const std::string source = ReadTrustAdminAdapterSource();

  CHECK(source.find("RE::BSFixedString Reset(RE::StaticFunctionTag*)") !=
        std::string::npos);
  CHECK(source.find("g_trustAdminService->StartFactoryReset()") !=
        std::string::npos);
  CHECK(source.find("g_trustAdminService->Reset()") == std::string::npos);

  CHECK(source.find("RE::BSFixedString ConfirmReset(RE::StaticFunctionTag*, "
                    "RE::BSFixedString akCode)") != std::string::npos);
  CHECK(source.find("g_trustAdminService->ConfirmFactoryReset(std::string_view("
                    "akCode))") != std::string::npos);

  CHECK(source.find("RE::BSFixedString ResetTrust(RE::StaticFunctionTag*)") !=
        std::string::npos);
  CHECK(source.find("g_trustAdminService->ResetTrust()") != std::string::npos);
}
