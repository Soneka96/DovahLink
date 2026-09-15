#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <filesystem>
#include <string>

using dovahlink::adapter::test_support::ReadSource;

///  Structural (source-text) checks for the composition wiring
///  `AdapterRuntime`'s constructor performs -- mirroring
///  `dovahlink_adapter_plugin_test.cpp`'s own structural tests before this
///  wiring moved out of `SKSEPluginLoad` and into `AdapterRuntime`. C++
///  semantics like "owned via `unique_ptr`, never a raw static local" cannot
///  be observed by a compiled test, only read from source text; behavior
///  this class exposes through its public API is covered separately by
///  `adapter_runtime_test.cpp`'s real compiled tests.

TEST_CASE("AdapterRuntime does not start the private IPC connection before "
          "supervisor discovery",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    CHECK(source.find("connection_->Start()") == std::string::npos);
    CHECK(source.find(".onTargetConnected") != std::string::npos);
    CHECK(source.find("*reader_, *launcher_, *connection_") !=
          std::string::npos);
}

TEST_CASE("AdapterRuntime routes a connected target to the session",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t onTargetConnected = source.find(".onTargetConnected =");
    REQUIRE(onTargetConnected != std::string::npos);
    std::size_t handleConnected =
        source.find("session_->HandleConnected(target);", onTargetConnected);

    REQUIRE(handleConnected != std::string::npos);
    CHECK(onTargetConnected < handleConnected);
}

TEST_CASE("AdapterRuntime routes a received message to the session",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t onMessageReceived = source.find(".onMessageReceived =");
    REQUIRE(onMessageReceived != std::string::npos);
    std::size_t handleMessage =
        source.find("session_->HandleMessage(message);", onMessageReceived);

    REQUIRE(handleMessage != std::string::npos);
    CHECK(onMessageReceived < handleMessage);
}

TEST_CASE("AdapterRuntime routes a decode failure to the session",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t onDecodeFailure = source.find(".onDecodeFailure =");
    REQUIRE(onDecodeFailure != std::string::npos);
    std::size_t handleDecodeFailure =
        source.find("session_->HandleDecodeFailure();", onDecodeFailure);

    REQUIRE(handleDecodeFailure != std::string::npos);
    CHECK(onDecodeFailure < handleDecodeFailure);
}

TEST_CASE("AdapterRuntime routes connection closing to the session",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t onClosing = source.find(".onClosing =");
    REQUIRE(onClosing != std::string::npos);
    std::size_t handleClosing =
        source.find("session_->HandleClosing();", onClosing);

    REQUIRE(handleClosing != std::string::npos);
    CHECK(onClosing < handleClosing);
}

TEST_CASE("AdapterRuntime notifies the supervisor when the connection "
          "reports the host lost",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t onDisconnected = source.find(".onDisconnected =");
    REQUIRE(onDisconnected != std::string::npos);
    std::size_t handleDisconnected =
        source.find("session_->HandleDisconnected();", onDisconnected);
    std::size_t attemptFinished = source.find(".onAttemptFinished =");
    std::size_t notifyConnectionLost = source.find(
        "supervisor_->NotifyConnectionLost(targetGeneration, outcome);",
        attemptFinished);

    REQUIRE(handleDisconnected != std::string::npos);
    REQUIRE(attemptFinished != std::string::npos);
    REQUIRE(notifyConnectionLost != std::string::npos);
    //  The session observes the physical disconnect before the completed
    //  attempt notifies the supervisor. The latter runs after the worker has
    //  marked itself restartable.
    CHECK(onDisconnected < handleDisconnected);
    CHECK(handleDisconnected < attemptFinished);
    CHECK(attemptFinished < notifyConnectionLost);
}

TEST_CASE("AdapterRuntime notifies the supervisor when a connection attempt "
          "fails",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t attemptFinished = source.find(".onAttemptFinished =");
    REQUIRE(attemptFinished != std::string::npos);
    std::size_t notifyConnectionLost = source.find(
        "supervisor_->NotifyConnectionLost(targetGeneration, outcome);",
        attemptFinished);

    REQUIRE(notifyConnectionLost != std::string::npos);
    CHECK(attemptFinished < notifyConnectionLost);
}

TEST_CASE("AdapterRuntime has no throwaway discovery verifier path",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    CHECK(source.find("AdapterHostHandshakeVerifier") == std::string::npos);
    CHECK(source.find("verifierSocket") == std::string::npos);
    CHECK(source.find("NotifyConnectionLost();") == std::string::npos);
    CHECK(source.find("NotifyConnectionLost(targetGeneration, outcome);") !=
          std::string::npos);
}

TEST_CASE("AdapterRuntime attaches the connection to the session only after "
          "both, and the supervisor, are constructed",
          "[plugin][structural]") {
    std::string source = ReadSource(DOVAHLINK_ADAPTER_RUNTIME_SOURCE_FILE);

    std::size_t connectionConstruction = source.find(
        "connection_ = std::make_unique<ipc::AdapterIpcConnection>");
    std::size_t supervisorConstruction = source.find(
        "supervisor_ = std::make_unique<process::AdapterHostSupervisor>");
    std::size_t attachConnection =
        source.find("session_->AttachConnection(*connection_);");

    REQUIRE(connectionConstruction != std::string::npos);
    REQUIRE(supervisorConstruction != std::string::npos);
    REQUIRE(attachConnection != std::string::npos);
    CHECK(connectionConstruction < attachConnection);
    CHECK(supervisorConstruction < attachConnection);
}

TEST_CASE("AdapterRuntime owns every collaborator in its graph through a "
          "unique_ptr member",
          "[plugin][structural]") {
    //  1B intentionally keeps this graph alive until Skyrim exits: the
    //  plugin composition path leaks the one AdapterRuntime instance itself
    //  (see dovahlink_adapter_plugin_test.cpp), so none of these members are
    //  ever destroyed in production. Each is still owned through a
    //  unique_ptr -- not a raw pointer or a value member -- so AdapterRuntime
    //  itself remains RAII-correct and destructible normally in tests.
    std::filesystem::path headerPath =
        std::filesystem::path(DOVAHLINK_ADAPTER_SOURCE_ROOT_DIR) / "plugin" /
        "adapter_runtime.hpp";
    std::string source = ReadSource(headerPath);

    for (const char* ownedMember :
         {"std::unique_ptr<capture::AdapterCaptureHandoffQueue> captureQueue_;",
          "std::unique_ptr<dispatch::AdapterNativeDispatcher> dispatcher_;",
          "std::unique_ptr<ipc::AdapterIpcSession> session_;",
          "std::unique_ptr<ipc::WinsockAdapterIpcSocket> socket_;",
          "std::unique_ptr<ipc::IpcFrameCodec> codec_;",
          "std::unique_ptr<process::FileAdapterHostRendezvousReader> reader_;",
          "std::unique_ptr<process::Win32AdapterHostProcessLauncher> "
          "launcher_;",
          "std::unique_ptr<ipc::AdapterIpcConnection> connection_;",
          "std::unique_ptr<process::AdapterHostSupervisor> supervisor_;"}) {
        INFO("checking " << ownedMember);
        CHECK(source.find(ownedMember) != std::string::npos);
    }
}
