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

TEST_CASE("the adapter plugin reports startup stages and catches load exceptions",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);

    std::size_t loadHelper = source.find("bool LoadAdapter(");
    std::size_t exportedLoad = source.find("SKSEPluginLoad(");
    std::size_t standardExceptionCatch =
        source.find("catch (const std::exception& exception)");
    std::size_t unknownExceptionCatch = source.find("catch (...)");
    std::size_t setupLogging =
        source.find("startupStage = \"SetupLogging\"");
    std::size_t compatibilityConfig =
        source.find("startupStage = \"Compatibility configuration\"");
    std::size_t listenerRegistration =
        source.find("startupStage = \"SKSE messaging listener registration\"");
    std::size_t startupComplete =
        source.find("startupStage = \"startup complete\"");

    REQUIRE(loadHelper != std::string::npos);
    REQUIRE(exportedLoad != std::string::npos);
    REQUIRE(standardExceptionCatch != std::string::npos);
    REQUIRE(unknownExceptionCatch != std::string::npos);
    REQUIRE(setupLogging != std::string::npos);
    REQUIRE(compatibilityConfig != std::string::npos);
    REQUIRE(listenerRegistration != std::string::npos);
    REQUIRE(startupComplete != std::string::npos);
    std::string loadBoundary = source.substr(exportedLoad);
    CHECK(loadBoundary.find("return LoadAdapter(skse, startupStage);") !=
          std::string::npos);
    CHECK(loadBoundary.find("catch (const std::exception& exception)") !=
          std::string::npos);
    CHECK(loadBoundary.find("catch (...)") != std::string::npos);
    CHECK(CountOccurrences(loadBoundary, "EmitStartupFailure(") == 2);
    CHECK(CountOccurrences(loadBoundary, "return false;") == 2);
    CHECK(source.find("EmitStartupMarker(startupStage);") !=
          std::string::npos);
    CHECK(loadHelper < exportedLoad);
    CHECK(setupLogging < compatibilityConfig);
    CHECK(compatibilityConfig < listenerRegistration);
    CHECK(listenerRegistration < startupComplete);
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

TEST_CASE("the adapter plugin sends a fresh play-context identity on both "
          "kNewGame and kPostLoadGame",
          "[plugin][structural]") {
    std::string rawSource = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
    std::string source = NormalizeWhitespace(rawSource);

    std::size_t newGameCheck =
        source.find("message->type==SKSE::MessagingInterface::kNewGame");
    std::size_t postLoadGameCheck =
        source.find("message->type==SKSE::MessagingInterface::kPostLoadGame");
    std::string sendCall = NormalizeWhitespace(
        "SendPlayContextChanged(playContextGenerator->Generate());");
    std::size_t sendPlayContextChanged = source.find(sendCall);

    REQUIRE(newGameCheck != std::string::npos);
    REQUIRE(postLoadGameCheck != std::string::npos);
    REQUIRE(sendPlayContextChanged != std::string::npos);
    //  Both message-type checks must guard the same one send call, not each
    //  have their own separate call -- CountOccurrences below proves that.
    CHECK(CountOccurrences(source, sendCall) == 1);
}

TEST_CASE("the adapter plugin ends the play context on kPreLoadGame, never "
          "sending a fresh identity there",
          "[plugin][structural]") {
    //  kPreLoadGame fires before the new state is actually loaded, so it ends
    //  the current context (no capture during the loading window can be
    //  attributed to either the old or the not-yet-existing new context);
    //  sending a fresh identity there would wrongly stamp captures with a
    //  context that does not yet correspond to real loaded state -- only
    //  kNewGame/kPostLoadGame (checked above) establish one.
    std::string rawSource = ReadSource(DOVAHLINK_ADAPTER_PLUGIN_SOURCE_FILE);
    std::string source = NormalizeWhitespace(rawSource);

    std::size_t preLoadGameCheck =
        source.find("message->type==SKSE::MessagingInterface::kPreLoadGame");
    REQUIRE(preLoadGameCheck != std::string::npos);

    //  Search from kPreLoadGame's own check to find the call it guards.
    std::string endedCall = NormalizeWhitespace("SendPlayContextEnded();");
    std::size_t sendPlayContextEnded =
        source.find(endedCall, preLoadGameCheck);
    REQUIRE(sendPlayContextEnded != std::string::npos);
    CHECK(sendPlayContextEnded > preLoadGameCheck);

    std::size_t newGameCheck =
        source.find("message->type==SKSE::MessagingInterface::kNewGame");
    REQUIRE(newGameCheck != std::string::npos);
    //  kPreLoadGame's own check and the SendPlayContextEnded() call it guards
    //  both come strictly before the kNewGame/kPostLoadGame check, matching
    //  the required "end the old context before a new one exists" ordering.
    CHECK(preLoadGameCheck < newGameCheck);
    CHECK(sendPlayContextEnded < newGameCheck);
}

TEST_CASE("MainMenuOpenedSink only ends the play context for an opening "
          "Main Menu event, never a closing event or a different menu",
          "[plugin][structural]") {
    //  The test target intentionally does not link CommonLibSSE-NG (see
    //  commonlib_adapter_native_capture_router_test.cpp's identical
    //  rationale for the same reason), so this pins the guard condition as a
    //  source-text invariant instead of a runtime assertion against a real
    //  RE::MenuOpenCloseEvent.
    const std::filesystem::path sourcePath =
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) /
        "plugin/commonlib_adapter_main_menu_sink.cpp";
    std::string source = NormalizeWhitespace(ReadSource(sourcePath));

    CHECK(source.find(NormalizeWhitespace(
              "if (event != nullptr && event->opening &&"
              "event->menuName == RE::MainMenu::MENU_NAME) { "
              "session_.SendPlayContextEnded(); } "
              "return RE::BSEventNotifyControl::kContinue;")) !=
          std::string::npos);
}

TEST_CASE("MainMenuOpenedSink keeps SKSE and Skyrim includes before its own headers",
          "[plugin][structural]") {
    const std::filesystem::path sourcePath =
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) /
        "plugin/commonlib_adapter_main_menu_sink.cpp";
    const std::filesystem::path headerPath =
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) /
        "plugin/commonlib_adapter_main_menu_sink.hpp";
    std::string source = ReadSource(sourcePath);
    std::string header = ReadSource(headerPath);

    std::size_t skseInclude = source.find("#include \"SKSE/SKSE.h\"");
    std::size_t skyrimInclude = source.find("#include \"RE/Skyrim.h\"");
    std::size_t ownHeader =
        source.find("#include \"plugin/commonlib_adapter_main_menu_sink.hpp\"");
    std::size_t headerSkyrimInclude = header.find("#include \"RE/Skyrim.h\"");

    REQUIRE(skseInclude != std::string::npos);
    REQUIRE(skyrimInclude != std::string::npos);
    REQUIRE(ownHeader != std::string::npos);
    REQUIRE(headerSkyrimInclude != std::string::npos);
    CHECK(skseInclude < skyrimInclude);
    CHECK(skyrimInclude < ownHeader);
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

TEST_CASE("the real-package-layout CTest fixture runs for every build configuration",
          "[plugin][structural][boundary]") {
    std::string source = ReadSource(
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) / "CMakeLists.txt");
    CHECK(source.find("add_test(NAME AssembleRealAdapterHostPackage") != std::string::npos);
    CHECK(source.find("FIXTURES_SETUP RealAdapterHostPackage") != std::string::npos);
    CHECK(source.find("FIXTURES_REQUIRED RealAdapterHostPackage") != std::string::npos);
    CHECK(source.find("SKIP_RETURN_CODE") == std::string::npos);
    CHECK(source.find("--configuration") == std::string::npos);
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
