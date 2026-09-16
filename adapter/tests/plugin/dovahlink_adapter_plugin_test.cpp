#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <filesystem>
#include <string>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Counts non-overlapping occurrences of `needle` in `haystack`.
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

TEST_CASE("the adapter plugin registers exactly one SKSE messaging listener",
          "[plugin][structural]") {
    //  SKSE allows exactly one MessagingInterface::RegisterListener call per
    //  plugin; a second call fails both registrations, per
    //  ai/context/skse/runtime-quirks.md. Structural pin, not a functional
    //  assertion.
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    CHECK(CountOccurrences(source, "RegisterListener(") == 1);
}

TEST_CASE("the adapter plugin defers host discovery startup to kDataLoaded",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::size_t dataLoadedCheck =
        source.find("message->type == SKSE::MessagingInterface::kDataLoaded");
    std::size_t runtimeStart = source.find("runtime->Start();");

    REQUIRE(dataLoadedCheck != std::string::npos);
    REQUIRE(runtimeStart != std::string::npos);
    CHECK(dataLoadedCheck < runtimeStart);
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

TEST_CASE("the adapter plugin registers the Papyrus status surface before "
          "serving any callback",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::size_t installPapyrus =
        source.find("InstallAdapterStatusPapyrusAdapter(");
    std::size_t registerListener = source.find("messaging->RegisterListener(");

    REQUIRE(installPapyrus != std::string::npos);
    REQUIRE(registerListener != std::string::npos);
    CHECK(installPapyrus < registerListener);
}

TEST_CASE("the adapter plugin registers the Papyrus trust-admin console "
          "surface before serving any callback",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::size_t installTrustAdmin =
        source.find("InstallAdapterTrustAdminPapyrusAdapter(");
    std::size_t registerListener = source.find("messaging->RegisterListener(");

    REQUIRE(installTrustAdmin != std::string::npos);
    REQUIRE(registerListener != std::string::npos);
    CHECK(installTrustAdmin < registerListener);
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

TEST_CASE("the adapter plugin derives and reuses one owner-lifetime identity",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::string normalizedSource = NormalizeWhitespace(source);

    CHECK(CountOccurrences(source, "DeriveOwnerLifetimeId(") == 1);
    CHECK(source.find("gOwnerLifetimeId = *ownerLifetimeId;") !=
          std::string::npos);
    //  Whitespace-normalized: pins that the derived owner-lifetime identity is
    //  reused for both rendezvous resolution and the startup context handed to
    //  AdapterRuntime, not clang-format's current line-wrap/indentation for
    //  either.
    CHECK(normalizedSource.find(NormalizeWhitespace(
              "ResolveDefaultRendezvousFilePath(*gOwnerLifetimeId)")) !=
          std::string::npos);
    CHECK(normalizedSource.find(NormalizeWhitespace(
              ".ownerLifetimeId = *gOwnerLifetimeId,")) != std::string::npos);
    CHECK(normalizedSource.find(NormalizeWhitespace(
              "*gOwnerLifetimeId).RequestShutdown();")) != std::string::npos);
}

TEST_CASE("the real-package-layout CTest fixture keys its skip decision on "
          "the Adapter's own build configuration, not the Host's",
          "[plugin][structural][boundary]") {
    //  DOVAHLINK_HOST_BUILD_CONFIGURATION exists so the Host and Adapter can be
    //  pointed at independently built configurations (it only selects which
    //  Host build folder DOVAHLINK_HOST_EXECUTABLE points at). Overriding it
    //  must never change whether a Release Adapter build's own package-layout
    //  test skips or fails -- that decision belongs to
    //  DOVAHLINK_ADAPTER_BUILD_CONFIGURATION, which is derived unconditionally
    //  from this configuration's own CMAKE_BUILD_TYPE and cannot be overridden.
    std::filesystem::path cmakeListsPath =
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) /
        "CMakeLists.txt";
    std::string source = ReadSource(cmakeListsPath);

    std::size_t addTestPos =
        source.find("add_test(NAME AssembleRealAdapterHostPackage");
    REQUIRE(addTestPos != std::string::npos);
    std::size_t addTestEnd = source.find(')', addTestPos);
    REQUIRE(addTestEnd != std::string::npos);
    std::string addTestBlock = source.substr(addTestPos, addTestEnd - addTestPos);

    CHECK(addTestBlock.find("DOVAHLINK_ADAPTER_BUILD_CONFIGURATION") !=
          std::string::npos);
    CHECK(addTestBlock.find("DOVAHLINK_HOST_BUILD_CONFIGURATION") ==
          std::string::npos);
}

TEST_CASE("the adapter plugin starts the host-discovery supervisor on "
          "kDataLoaded",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::size_t dataLoadedCheck =
        source.find("message->type == SKSE::MessagingInterface::kDataLoaded");
    std::size_t runtimeStart = source.find("runtime->Start();");

    REQUIRE(dataLoadedCheck != std::string::npos);
    REQUIRE(runtimeStart != std::string::npos);
    CHECK(dataLoadedCheck < runtimeStart);
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
    for (const char* loaderUnsafeOperation :
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
    //  The first process-lifetime `new` after every fatal startup guard:
    //  CommonLibAdapterTaskMarshaller, constructed just before AdapterRuntime
    //  itself.
    std::size_t workerConstruction = source.find(
        "new dovahlink::adapter::runtime::CommonLibAdapterTaskMarshaller");

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

TEST_CASE("the adapter plugin constructs exactly one process-lifetime "
          "AdapterRuntime and never a destructible local instance",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    //  A function-local (non-pointer) AdapterRuntime would be destroyed
    //  during DLL detach, which could join its worker-owning collaborators
    //  under the loader lock. Windows reclaims the leaked heap allocation,
    //  its threads, and its sockets when Skyrim exits instead.
    CHECK(source.find("static dovahlink::adapter::plugin::AdapterRuntime "
                      "runtime") == std::string::npos);
    CHECK(CountOccurrences(
              source, "new dovahlink::adapter::plugin::AdapterRuntime(") == 1);
}
