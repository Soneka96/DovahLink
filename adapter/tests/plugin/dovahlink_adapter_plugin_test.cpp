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

TEST_CASE("the adapter plugin defers host discovery startup to kDataLoaded",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t dataLoadedCheck =
      source.find("message->type == SKSE::MessagingInterface::kDataLoaded");
  std::size_t supervisorStart = source.find("supervisor->Start();");

  REQUIRE(dataLoadedCheck != std::string::npos);
  REQUIRE(supervisorStart != std::string::npos);
  CHECK(dataLoadedCheck < supervisorStart);
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
      source.find("session->AttachConnection(*connection);");
  std::size_t installPapyrus =
      source.find("InstallAdapterStatusPapyrusAdapter(*session);");
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
  std::size_t setupLogging = source.find("SetupLogging();");
  std::size_t returnTrue = source.find("return true;");

  REQUIRE(messagingCheck != std::string::npos);
  REQUIRE(returnFalse != std::string::npos);
  REQUIRE(returnTrue != std::string::npos);
  REQUIRE(setupLogging != std::string::npos);
  //  The failure return belongs to the null-interface guard, before the
  //  listener is ever registered. Asynchronous logging is also configured
  //  only after every fatal startup guard has passed.
  CHECK(returnFalse < registerListener);
  CHECK(returnFalse < setupLogging);
  CHECK(setupLogging < registerListener);
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
                  "process", "runtime"}) {
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

TEST_CASE("the adapter plugin starts the host-discovery supervisor on "
          "kDataLoaded",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t dataLoadedCheck =
      source.find("message->type == SKSE::MessagingInterface::kDataLoaded");
  std::size_t supervisorStart = source.find("supervisor->Start();");

  REQUIRE(dataLoadedCheck != std::string::npos);
  REQUIRE(supervisorStart != std::string::npos);
  CHECK(dataLoadedCheck < supervisorStart);
}

TEST_CASE("the adapter plugin does not start IPC before supervisor discovery",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  CHECK(source.find("connection->Start();") == std::string::npos);
  CHECK(source.find("*peerProofProvider, *connection") != std::string::npos);
}

TEST_CASE("the adapter plugin notifies the supervisor when the connection "
          "reports the host lost",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t onDisconnected = source.find(".onDisconnected =");
  REQUIRE(onDisconnected != std::string::npos);
  std::size_t handleDisconnected =
      source.find("session->HandleDisconnected();", onDisconnected);
  std::size_t notifyConnectionLost =
      source.find("supervisor->NotifyConnectionLost();", onDisconnected);

  REQUIRE(handleDisconnected != std::string::npos);
  REQUIRE(notifyConnectionLost != std::string::npos);
  //  Both calls belong to this one callback, in this order: the session
  //  observes the disconnect first, then the supervisor is told to run
  //  another discovery round.
  CHECK(onDisconnected < handleDisconnected);
  CHECK(handleDisconnected < notifyConnectionLost);
}

TEST_CASE("DllMain signals shutdown without calling the blocking ordered "
          "shutdown sequence, joining a thread, or waiting on a handle",
          "[plugin][structural]") {
  //  DLL_PROCESS_DETACH runs under the loader lock; only the non-blocking
  //  RequestShutdown() signal is safe there. This structural check pins
  //  that DllMain's own body never grows a call to the blocking orchestrator
  //  method or any thread join/handle wait.
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t dllMain = source.find("DllMain(");
  REQUIRE(dllMain != std::string::npos);
  std::string dllMainOnward = source.substr(dllMain);

  std::size_t reasonCheck = dllMainOnward.find("reason == DLL_PROCESS_DETACH");
  std::size_t requestShutdown = dllMainOnward.find("RequestShutdown()");
  REQUIRE(reasonCheck != std::string::npos);
  REQUIRE(requestShutdown != std::string::npos);
  //  The signal is gated on DLL_PROCESS_DETACH specifically, not fired
  //  unconditionally for every DllMain reason (attach, thread attach/detach).
  CHECK(reasonCheck < requestShutdown);
  std::size_t bodyStart = dllMainOnward.find('{');
  std::size_t bodyEnd = dllMainOnward.find('}', bodyStart);
  REQUIRE(bodyStart != std::string::npos);
  REQUIRE(bodyEnd != std::string::npos);
  std::string body = dllMainOnward.substr(bodyStart, bodyEnd - bodyStart + 1);
  CHECK(body.find("RequestShutdown()") != std::string::npos);
  for (const char *loaderUnsafeOperation :
       {"RunOrderedShutdown", ".join(", "WaitForSingleObject", ".Stop(",
        "AwaitExitOrTerminate", "RequestStop(", "Release(", "std::thread",
        "CreateThread", "Sleep(", "CloseHandle", "TerminateProcess",
        "SetupLogging", "new ", "delete "}) {
    INFO("checking " << loaderUnsafeOperation);
    CHECK(body.find(loaderUnsafeOperation) == std::string::npos);
  }
}

TEST_CASE("the adapter plugin fails load cleanly when the rendezvous file "
          "path or the host executable path cannot be resolved",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  std::size_t rendezvousCheck = source.find("!rendezvousPath.has_value()");
  std::size_t rendezvousReturnFalse =
      source.find("return false;", rendezvousCheck);
  std::size_t executableCheck = source.find("!hostExecutablePath.has_value()");
  std::size_t executableReturnFalse =
      source.find("return false;", executableCheck);
  std::size_t messagingCheck = source.find("if (!messaging)");
  std::size_t messagingReturnFalse =
      source.find("return false;", messagingCheck);
  std::size_t workerConstruction = source.find(
      "new dovahlink::adapter::capture::AdapterCaptureHandoffQueue");

  REQUIRE(rendezvousCheck != std::string::npos);
  REQUIRE(rendezvousReturnFalse != std::string::npos);
  REQUIRE(executableCheck != std::string::npos);
  REQUIRE(executableReturnFalse != std::string::npos);
  REQUIRE(messagingCheck != std::string::npos);
  REQUIRE(messagingReturnFalse != std::string::npos);
  REQUIRE(workerConstruction != std::string::npos);
  //  Every failure guard runs before any thread-owning process-lifetime
  //  object is constructed, so a rejected load can safely unload the DLL.
  CHECK(rendezvousCheck < rendezvousReturnFalse);
  CHECK(rendezvousReturnFalse < workerConstruction);
  CHECK(executableCheck < executableReturnFalse);
  CHECK(executableReturnFalse < workerConstruction);
  CHECK(messagingCheck < messagingReturnFalse);
  CHECK(messagingReturnFalse < workerConstruction);
}

TEST_CASE("the adapter plugin keeps worker-owning runtime objects out of DLL "
          "detach destruction",
          "[plugin][structural]") {
  std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

  //  1B intentionally keeps these objects alive until Skyrim exits. A
  //  function-local static object would be destroyed during DLL detach and
  //  could join its worker under the loader lock.
  for (const char *automaticWorkerType :
       {"static dovahlink::adapter::capture::AdapterCaptureHandoffQueue",
        "static dovahlink::adapter::ipc::AdapterIpcConnection",
        "static dovahlink::adapter::process::AdapterHostSupervisor"}) {
    INFO("checking " << automaticWorkerType);
    CHECK(source.find(automaticWorkerType) == std::string::npos);
  }

  CHECK(
      source.find("static auto *captureQueue =\n      new "
                  "dovahlink::adapter::capture::AdapterCaptureHandoffQueue") !=
      std::string::npos);
  CHECK(source.find("static auto *connection = new dovahlink::adapter::ipc::"
                    "AdapterIpcConnection") != std::string::npos);
  CHECK(source.find("static auto *supervisor =\n      static_cast<"
                    "dovahlink::adapter::process::AdapterHostSupervisor *>") !=
        std::string::npos);
  CHECK(source.find("supervisor = new "
                    "dovahlink::adapter::process::AdapterHostSupervisor") !=
        std::string::npos);
}
