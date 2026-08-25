#include <catch2/catch_test_macros.hpp>

#include <array>
#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the Papyrus adapter's own source for structural registration
///  assertions.
std::string ReadTrustAdminAdapterSource() {
    std::ifstream file(DOVAHLINK_TRUST_ADMIN_PAPYRUS_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  Papyrus VM registration requires a running Skyrim process, so this test
//  protects the adapter surface structurally instead: the canonical List/Help
//  functions must be forwarded and the old split listing functions must not be
//  registered again.
TEST_CASE(
    "Trust-admin Papyrus registration exposes the canonical listing surface",
    "[game_state][registration]") {
    const std::string source = ReadTrustAdminAdapterSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "RE::BSFixedString List(RE::StaticFunctionTag*, "
                "RE::BSFixedString akScope)"));
    CHECK(source.find(
              "g_trustDeviceAdminService->List(std::string_view(akScope))") !=
          std::string::npos);
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "RE::BSFixedString Help(RE::StaticFunctionTag*)"));
    CHECK(source.find("g_trustDeviceAdminService->Help()") !=
          std::string::npos);
    CHECK(source.find(
              "g_trustDeviceAdminService->RevokeByShortId(") !=
          std::string::npos);
    CHECK(source.find(
              "std::string_view(akId), std::chrono::steady_clock::now())") !=
          std::string::npos);
    CHECK(dovahlink::test_support::ContainsSourceText(
        source,
        "g_trustDeviceAdminService->BlockByShortId(std::string_view(akId), "
        "std::chrono::steady_clock::now())"));
    CHECK(source.find(
              "g_trustDeviceAdminService->UnblockByShortId(std::string_view(akId))") !=
          std::string::npos);
    CHECK(source.find(
              "g_trustDeviceAdminService->ForgetByShortId(std::string_view(akId))") !=
          std::string::npos);

    for (const auto& functionName :
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

//  Reset now only starts the confirmation challenge; the destructive wipe moved
//  to ConfirmReset, and Reset Trust's non-destructive bulk revoke is its own
//  separate function.
TEST_CASE("Trust-admin Papyrus registration exposes Reset Trust and Factory "
          "Reset confirmation",
          "[game_state][registration]") {
    const std::string source = ReadTrustAdminAdapterSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "RE::BSFixedString Reset(RE::StaticFunctionTag*)"));
    CHECK(source.find("g_trustResetService->StartFactoryReset()") !=
          std::string::npos);
    CHECK(source.find("g_trustResetService->Reset()") == std::string::npos);

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "RE::BSFixedString ConfirmReset(RE::StaticFunctionTag*, "
                "RE::BSFixedString akCode)"));
    CHECK(source.find("g_trustResetService->ConfirmFactoryReset(std::string_view("
                      "akCode))") != std::string::npos);

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "RE::BSFixedString ResetTrust(RE::StaticFunctionTag*)"));
    CHECK(source.find("g_trustResetService->ResetTrust()") !=
          std::string::npos);
}
