#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Counts non-overlapping occurrences of a substring in source text.
///  @param haystack Text to search.
///  @param needle Substring to count.
std::size_t CountOccurrences(const std::string& haystack,
                             const std::string& needle) {
    std::size_t count = 0;
    std::size_t pos = 0;
    while ((pos = haystack.find(needle, pos)) != std::string::npos) {
        ++count;
        pos += needle.size();
    }
    return count;
}

///  Reads the plugin entry point's own source text for structural assertions.
std::string ReadPluginSource() {
    std::ifstream file(DOVAHLINK_PLUGIN_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  SKSE::MessagingInterface::RegisterListener may be called at most once per
//  plugin; a second call silently breaks both registrations instead of
//  erroring loudly (see ai/context/skse/testing.md and the comment above the
//  registration call in dovahlink_bridge_plugin.cpp). That failure mode plays
//  out only inside a running SKSE process, so it cannot be caught by exercising
//  plugin behavior in a unit test. This test instead enforces the invariant
//  structurally, by asserting there is exactly one call site in the plugin
//  entry point's source text -- every new messaging-interface concern must be
//  added inside that single listener's dispatch, never via another
//  RegisterListener call.
TEST_CASE("SKSEPluginLoad registers the SKSE messaging listener exactly once",
          "[plugin][registration]") {
    std::string source = ReadPluginSource();

    //  Text search only, not a C++ parse: a mention of "RegisterListener(" inside
    //  a comment or string literal would also count. Acceptable for this file,
    //  which does not otherwise reference the name in prose; raise the check to
    //  a real parse only if that stops being true.
    CHECK(CountOccurrences(source, "RegisterListener(") == 1);
}

//  SKSE::GetPluginHandle() (used internally by MessagingInterface::
//  RegisterListener, SerializationInterface's callbacks, and every other
//  SKSE::Get*Interface()-based registration) returns an unset sentinel until
//  SKSE::Init has recorded this plugin's real handle -- confirmed directly
//  from CommonLibSSE-NG's own source (APIStorage::pluginHandle defaults to
//  -1 and is only assigned inside SKSE::Init) and empirically in-game (SKSE
//  logged "Failed to register messaging listener for SKSE" and disabled the
//  plugin before this call existed). That failure mode plays out only inside
//  a running SKSE process, so it cannot be caught by exercising plugin
//  behavior in a unit test; this asserts the fix structurally instead.
TEST_CASE("SKSEPluginLoad calls SKSE::Init before any interface registration "
          "that depends on it",
          "[plugin][registration]") {
    std::string source = ReadPluginSource();

    std::size_t initPos = dovahlink::test_support::FindSourceText(source, "SKSE::Init(");
    REQUIRE(initPos != std::string::npos);

    for (const std::string& dependentCall :
         {std::string("RegisterListener("), std::string("SetRevertCallback(")}) {
        std::size_t callPos =
            dovahlink::test_support::FindSourceText(source, dependentCall);
        REQUIRE(callPos != std::string::npos);
        CHECK(initPos < callPos);
    }
}

TEST_CASE("SKSEPluginLoad gives read-only consumers the context reader adapter",
          "[plugin][composition]") {
    std::string source = ReadPluginSource();

    std::size_t readerPos =
        source.find("activePlayContextReader(playContextLifecycle)");
    REQUIRE(readerPos != std::string::npos);

    std::size_t levelSinkPos =
        source.find("ActivePlayContextLevelSink levelSink");
    REQUIRE(levelSinkPos != std::string::npos);
    CHECK(source.find("playContextLifecycle);", levelSinkPos) !=
          std::string::npos);

    //  `HandshakeHandler` is the read-only consumer that actually receives
    //  `activePlayContextReader` today (`ConnectionSession` also holds it, but
    //  `BridgeWorkerPool` itself no longer takes any play-context capability
    //  directly -- it only holds the already-composed `ConnectionSession`).
    std::size_t handshakeHandlerPos =
        source.find("HandshakeHandler handshakeHandler");
    REQUIRE(handshakeHandlerPos != std::string::npos);
    CHECK(readerPos < handshakeHandlerPos);
    CHECK(source.find("activePlayContextReader,", handshakeHandlerPos) !=
          std::string::npos);

    std::size_t workerPoolPos = source.find("BridgeWorkerPool bridgeWorkerPool");
    REQUIRE(workerPoolPos != std::string::npos);
    CHECK(readerPos < workerPoolPos);
    CHECK(handshakeHandlerPos < workerPoolPos);

    std::size_t lifecycleSinkPos =
        source.find("CommonLibGameLifecycleSink lifecycleSink");
    REQUIRE(lifecycleSinkPos != std::string::npos);
    std::size_t lifecyclePos =
        source.find("PlayContextLifecycle playContextLifecycle;");
    REQUIRE(lifecyclePos != std::string::npos);
    CHECK(lifecyclePos < lifecycleSinkPos);
    CHECK(source.find("playContextLifecycle);", lifecycleSinkPos) !=
          std::string::npos);

    std::size_t lifecycleRegisterPos =
        source.find("lifecycleSinkContract.Register(", lifecycleSinkPos);
    REQUIRE(lifecycleRegisterPos != std::string::npos);
    CHECK(dovahlink::test_support::FindSourceText(
              source,
              "ICommonLibGameLifecycleSink& lifecycleSinkContract = lifecycleSink") !=
          std::string::npos);
    std::size_t listenerRegisterPos = source.find("RegisterListener(");
    REQUIRE(listenerRegisterPos != std::string::npos);
    CHECK(lifecycleRegisterPos < listenerRegisterPos);
    std::size_t revertCallbackPos = source.find("SetRevertCallback(");
    REQUIRE(revertCallbackPos != std::string::npos);
    CHECK(lifecycleRegisterPos < revertCallbackPos);
}

//  BridgeCallbackRegistry's own contract and forwarding behavior are proven
//  directly by application/bridge_callback_registry_test.cpp. This structural
//  check only proves the composition root wires it to the runtime sink.
TEST_CASE("SKSEPluginLoad wires the callback registry to the runtime sink",
          "[plugin][composition]") {
    std::string source = ReadPluginSource();

    CHECK(source.find("BridgeCallbackRegistry callbackRegistry(\n        "
                      "levelIncreaseSink);") != std::string::npos);
}

TEST_CASE("SKSEPluginLoad passes trust administration through service contracts",
          "[plugin][composition]") {
    std::string source = ReadPluginSource();

    CHECK(source.find(
              "ITrustDeviceAdminService&\n        trustDeviceAdminServiceContract") !=
          std::string::npos);
    CHECK(source.find("ITrustResetService& trustResetServiceContract") !=
          std::string::npos);
    CHECK(source.find(
              "trustDeviceAdminServiceContract, trustResetServiceContract") !=
          std::string::npos);
}
