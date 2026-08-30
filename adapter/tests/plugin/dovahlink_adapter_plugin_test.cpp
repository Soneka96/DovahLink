#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <filesystem>
#include <string>

using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Counts non-overlapping occurrences of `needle` in `haystack`.
std::size_t CountOccurrences(const std::string &haystack,
                             const std::string &needle) {
  std::size_t count = 0;
  std::size_t position = 0;
  while ((position = haystack.find(needle, position)) != std::string::npos) {
    ++count;
    position += needle.size();
  }
  return count;
}

} //  namespace

TEST_CASE("the adapter plugin registers exactly one SKSE messaging listener",
          "[plugin][structural]") {
  //  SKSE allows exactly one MessagingInterface::RegisterListener call per
  //  plugin; a second call fails both registrations, per
  //  ai/context/skse/runtime-quirks.md. Structural pin, not a functional
  //  assertion, mirroring
  //  bridge/plugin/dovahlink_bridge_plugin_registration_test.cpp.
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  CHECK(CountOccurrences(source, "RegisterListener(") == 1);
}

TEST_CASE("the adapter plugin defers connection startup to kDataLoaded",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t dataLoadedCheck =
      source.find("message->type == SKSE::MessagingInterface::kDataLoaded");
  std::size_t connectionStart = source.find("connection.Start();");

  REQUIRE(dataLoadedCheck != std::string::npos);
  REQUIRE(connectionStart != std::string::npos);
  CHECK(dataLoadedCheck < connectionStart);
}

TEST_CASE("the adapter plugin calls SKSE::Init before registering the "
          "messaging listener",
          "[plugin][structural]") {
  //  SKSE-QUIRK:
  //  ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t skseInit = source.find("SKSE::Init(skse);");
  std::size_t registerListener = source.find("messaging->RegisterListener(");

  REQUIRE(skseInit != std::string::npos);
  REQUIRE(registerListener != std::string::npos);
  CHECK(skseInit < registerListener);
}

TEST_CASE("the adapter plugin attaches the connection to the session and "
          "registers the Papyrus status surface before serving any "
          "callback",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t attachConnection =
      source.find("session.AttachConnection(connection);");
  std::size_t installPapyrus =
      source.find("InstallAdapterStatusPapyrusAdapter(session);");
  std::size_t registerListener = source.find("messaging->RegisterListener(");

  REQUIRE(attachConnection != std::string::npos);
  REQUIRE(installPapyrus != std::string::npos);
  REQUIRE(registerListener != std::string::npos);
  CHECK(attachConnection < registerListener);
  CHECK(installPapyrus < registerListener);
}

TEST_CASE("the adapter plugin fails load cleanly when SKSE's messaging "
          "interface is unavailable, and otherwise returns success",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t messagingCheck = source.find("if (!messaging)");
  std::size_t returnFalse = source.find("return false;", messagingCheck);
  std::size_t registerListener = source.find("messaging->RegisterListener(");
  std::size_t returnTrue = source.find("return true;");

  REQUIRE(messagingCheck != std::string::npos);
  REQUIRE(returnFalse != std::string::npos);
  REQUIRE(returnTrue != std::string::npos);
  //  The failure return belongs to the null-interface guard, before the
  //  listener is ever registered; the success return comes after it.
  CHECK(returnFalse < registerListener);
  CHECK(returnTrue > registerListener);
}

TEST_CASE("adapter/CMakeLists.txt never links or builds a bridge/ target",
          "[plugin][structural][boundary]") {
  //  Proof obligation: "Independent adapter configuration/build does not
  //  link or include bridge/", per
  //  plans/stage-3-thin-native-adapter-private-ipc/03-native-adapter-core.md.
  //  Checks actual build directives, not prose: this file's own comments
  //  legitimately reference bridge/CMakeLists.txt as documentation (the same
  //  way ai/context/adapter/architecture.md does), which is not a link or
  //  include.
  std::filesystem::path cmakeListsPath =
      std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) /
      "CMakeLists.txt";
  std::string source = ReadSource(cmakeListsPath);

  CHECK(source.find("add_subdirectory") == std::string::npos);
  CHECK(source.find("dovahlink_bridge") == std::string::npos);
}

TEST_CASE("no adapter production source file includes a bridge/ header",
          "[plugin][structural][boundary]") {
  std::filesystem::path root{DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR};

  int fileCount = 0;
  for (const char *subdirectory :
       std::array{"capture", "dispatch", "identity", "ipc", "papyrus", "plugin",
                  "runtime"}) {
    std::filesystem::path directory = root / subdirectory;
    REQUIRE(std::filesystem::exists(directory));

    for (const auto &entry : std::filesystem::directory_iterator(directory)) {
      if (entry.path().extension() != ".hpp" &&
          entry.path().extension() != ".cpp") {
        continue;
      }
      ++fileCount;

      std::string text = ReadSource(entry.path());
      INFO("checking " << entry.path().filename().string());
      CHECK(text.find("#include \"bridge/") == std::string::npos);
      CHECK(text.find("#include <bridge/") == std::string::npos);
    }
  }

  CHECK(fileCount > 0);
}
