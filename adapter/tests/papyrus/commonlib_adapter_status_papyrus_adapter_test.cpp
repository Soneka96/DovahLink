#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <filesystem>
#include <string>

using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("no adapter/papyrus header includes a Skyrim or SKSE runtime "
          "header",
          "[papyrus][structural]") {
  //  Structural pin, not a functional assertion: the header must stay
  //  CommonLib-free so IAdapterIpcSession's own module never has to know
  //  about Papyrus. Only the implementation's .cpp is allowed to reach
  //  CommonLib.
  std::filesystem::path papyrusDir{DOVAHLINK_ADAPTER_PAPYRUS_DIR};
  REQUIRE(std::filesystem::exists(papyrusDir));

  int headerCount = 0;
  for (const auto &entry : std::filesystem::directory_iterator(papyrusDir)) {
    if (entry.path().extension() != ".hpp") {
      continue;
    }
    ++headerCount;

    std::string text = ReadSource(entry.path());
    INFO("checking " << entry.path().filename().string());
    CHECK(text.find("RE/") == std::string::npos);
    CHECK(text.find("SKSE/") == std::string::npos);
  }

  CHECK(headerCount > 0);
}

TEST_CASE("CommonLibAdapterStatusPapyrusAdapter includes RE/Skyrim.h and "
          "SKSE/SKSE.h before its own header",
          "[papyrus][commonlib_adapter_status_papyrus_adapter][structural]") {
  //  The test target intentionally does not link CommonLibSSE-NG. This
  //  structural check protects the include-order rule
  //  ai/context/skse/cpp-style.md requires for any file that directly
  //  includes an RE/... or SKSE/... runtime header.
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE);

  std::size_t reInclude = source.find("#include \"RE/Skyrim.h\"");
  std::size_t skseInclude = source.find("#include \"SKSE/SKSE.h\"");
  std::size_t ownHeaderInclude = source.find(
      "#include \"papyrus/commonlib_adapter_status_papyrus_adapter.hpp\"");

  REQUIRE(reInclude != std::string::npos);
  REQUIRE(skseInclude != std::string::npos);
  REQUIRE(ownHeaderInclude != std::string::npos);
  CHECK(reInclude < ownHeaderInclude);
  CHECK(skseInclude < ownHeaderInclude);
}

TEST_CASE("CommonLibAdapterStatusPapyrusAdapter forwards to "
          "IAdapterIpcSession::IsHostAvailable and reports an explicit "
          "not-ready status",
          "[papyrus][commonlib_adapter_status_papyrus_adapter][structural]") {
  //  Proves this concept's Papyrus proof obligation textually: the query
  //  reports an explicit unavailable status rather than a plausible
  //  default, and never itself performs a connection attempt -- the source
  //  contains no reconnect, retry, or Connect call at all.
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE);

  CHECK(source.find("g_session->IsHostAvailable()") != std::string::npos);
  CHECK(source.find("\"host not ready\"") != std::string::npos);
  CHECK(source.find("\"host ready\"") != std::string::npos);
  CHECK(source.find("Connect") == std::string::npos);
}

TEST_CASE("CommonLibAdapterStatusPapyrusAdapter reports explicit "
          "unavailable and internal-error statuses without crashing",
          "[papyrus][commonlib_adapter_status_papyrus_adapter][structural]") {
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE);

  CHECK(source.find("RE::BSFixedString GetHostStatus(RE::StaticFunctionTag "
                    "*)") != std::string::npos);
  CHECK(source.find("if (!g_session)") != std::string::npos);
  CHECK(source.find("\"DovahLink adapter status is unavailable.\"") !=
        std::string::npos);
  CHECK(source.find("catch (...)") != std::string::npos);
  CHECK(
      source.find("\"DovahLink adapter status check failed unexpectedly.\"") !=
      std::string::npos);
}

TEST_CASE("CommonLibAdapterStatusPapyrusAdapter registers GetHostStatus and "
          "handles a missing or failed Papyrus interface",
          "[papyrus][commonlib_adapter_status_papyrus_adapter][structural]") {
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE);

  CHECK(source.find("vm->RegisterFunction(\"GetHostStatus\", "
                    "\"DovahLinkAdapterStatus\",") != std::string::npos);
  CHECK(source.find("if (!papyrusInterface)") != std::string::npos);
  CHECK(source.find("if (!papyrusInterface->Register(RegisterFunctions))") !=
        std::string::npos);
}

TEST_CASE("CommonLibAdapterStatusPapyrusAdapter stores the session behind "
          "its interface contract, not the concrete type",
          "[papyrus][commonlib_adapter_status_papyrus_adapter][structural]") {
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_STATUS_PAPYRUS_ADAPTER_SOURCE_FILE);

  CHECK(source.find("ipc::IAdapterIpcSession *g_session") != std::string::npos);
  CHECK(source.find("ipc::AdapterIpcSession *g_session") == std::string::npos);
}
