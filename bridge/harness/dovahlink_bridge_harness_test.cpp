#include "application/bridge_config.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "protocol/messages.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/system/error_code.hpp>

#include <windows.h>

#include <chrono>
#include <cstring>
#include <optional>
#include <string>
#include <string_view>
#include <thread>
#include <utility>

// Launches the harness as a real process, drives its protocol over a real
// loopback socket, changes level through stdin, and verifies clean shutdown.

namespace {

constexpr const char* kHarnessExePath = DOVAHLINK_HARNESS_EXE;
constexpr const char* kValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

// Builds an ANSI environment block from the test process environment, with
// DOVAHLINK_BRIDGE_TOKEN replaced by
// `tokenValue` (or omitted entirely if nullopt, to test the missing-token
// path) rather than left set globally -- Catch2 runs test cases sequentially
// in one process, so mutating this process's own environment would leak
// between tests.
/// Builds the complete environment block used by the child process.
std::string BuildEnvironmentBlock(std::optional<std::string> tokenValue) {
    std::string block;
    LPCH currentEnv = GetEnvironmentStringsA();
    REQUIRE(currentEnv != nullptr);
    std::string tokenPrefix = std::string(dovahlink::application::kTokenEnvVar) + "=";
    for (LPCH entry = currentEnv; *entry != '\0'; entry += std::strlen(entry) + 1) {
        std::string_view line(entry);
        if (!line.starts_with(tokenPrefix)) {
            block.append(line);
            block.push_back('\0');
        }
    }
    FreeEnvironmentStringsA(currentEnv);
    if (tokenValue.has_value()) {
        block.append(tokenPrefix);
        block.append(*tokenValue);
        block.push_back('\0');
    }
    block.push_back('\0');
    return block;
}

/// Owns a harness subprocess and its redirected standard handles.
class HarnessProcess {
public:
    /// Launches the executable with the supplied token environment value.
    HarnessProcess(const std::string& exePath, std::optional<std::string> tokenValue) {
        SECURITY_ATTRIBUTES sa{};
        sa.nLength = sizeof(sa);
        sa.bInheritHandle = TRUE;

        HANDLE childStdinRead = nullptr;
        HANDLE childStdoutWrite = nullptr;
        REQUIRE(CreatePipe(&childStdinRead, &stdinWrite_, &sa, 0));
        REQUIRE(SetHandleInformation(stdinWrite_, HANDLE_FLAG_INHERIT, 0));
        REQUIRE(CreatePipe(&stdoutRead_, &childStdoutWrite, &sa, 0));
        REQUIRE(SetHandleInformation(stdoutRead_, HANDLE_FLAG_INHERIT, 0));

        STARTUPINFOA si{};
        si.cb = sizeof(si);
        si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdInput = childStdinRead;
        si.hStdOutput = childStdoutWrite;
        si.hStdError = childStdoutWrite;

        PROCESS_INFORMATION pi{};
        std::string commandLine = "\"" + exePath + "\"";
        std::string environmentBlock = BuildEnvironmentBlock(std::move(tokenValue));
        BOOL started = CreateProcessA(nullptr, commandLine.data(), nullptr, nullptr, /*bInheritHandles=*/TRUE, 0,
                                       environmentBlock.data(), nullptr, &si, &pi);

        // Close the parent's copies of the child's ends, and clean up
        // fully on a failed launch, before REQUIRE can throw and skip the
        // destructor (a constructor that throws leaves the object
        // considered never-constructed, so ~HarnessProcess never runs for
        // it -- these handles are the only ones that would otherwise leak).
        CloseHandle(childStdinRead);
        CloseHandle(childStdoutWrite);
        if (!started) {
            CloseHandle(stdinWrite_);
            CloseHandle(stdoutRead_);
            stdinWrite_ = nullptr;
            stdoutRead_ = nullptr;
        }
        REQUIRE(started);
        process_ = pi.hProcess;
        thread_ = pi.hThread;
    }

    /// Closes redirected handles and waits briefly for the child process.
    ~HarnessProcess() {
        CloseHandle(stdinWrite_);
        CloseHandle(stdoutRead_);
        if (process_) {
            WaitForSingleObject(process_, 5000);
            CloseHandle(process_);
        }
        if (thread_) {
            CloseHandle(thread_);
        }
    }

    /// Prevents copying process handles.
    HarnessProcess(const HarnessProcess&) = delete;
    /// Prevents assigning process handles.
    HarnessProcess& operator=(const HarnessProcess&) = delete;

    /// Writes one newline-terminated command to the child stdin.
    void WriteLine(const std::string& line) {
        std::string withNewline = line + "\n";
        DWORD written = 0;
        REQUIRE(WriteFile(stdinWrite_, withNewline.data(), static_cast<DWORD>(withNewline.size()), &written,
                           nullptr));
    }

    /// Reads one line from child stdout, returning empty text on pipe closure.
    std::string ReadLine() {
        std::string result;
        char ch = '\0';
        DWORD bytesRead = 0;
        while (ReadFile(stdoutRead_, &ch, 1, &bytesRead, nullptr) && bytesRead == 1) {
            if (ch == '\n') {
                break;
            }
            if (ch != '\r') {
                result += ch;
            }
        }
        return result;
    }

    /// Returns whether the process exited within `timeout`.
    bool WaitForExit(std::chrono::milliseconds timeout) {
        auto result = WaitForSingleObject(process_, static_cast<DWORD>(timeout.count()));
        return result == WAIT_OBJECT_0;
    }

    /// Returns the child exit code after it has exited.
    [[nodiscard]] int ExitCode() const {
        DWORD exitCode = 0;
        REQUIRE(GetExitCodeProcess(process_, &exitCode));
        return static_cast<int>(exitCode);
    }

private:
    /// Child process handle.
    HANDLE process_ = nullptr;
    /// Child primary-thread handle.
    HANDLE thread_ = nullptr;
    /// Parent write handle for child stdin.
    HANDLE stdinWrite_ = nullptr;
    /// Parent read handle for child stdout and stderr.
    HANDLE stdoutRead_ = nullptr;
};

/// Reads and decodes one protocol envelope from the client socket.
dovahlink::protocol::Envelope ClientReadEnvelope(boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& ws) {
    boost::beast::flat_buffer buffer;
    boost::system::error_code ec;
    ws.read(buffer, ec);
    REQUIRE_FALSE(ec);
    auto parsed = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(buffer.data()));
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

/// Writes one text protocol message to the client socket.
void ClientWriteText(boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& ws, const std::string& text) {
    ws.text(true);
    boost::system::error_code ec;
    ws.write(boost::asio::buffer(text), ec);
    REQUIRE_FALSE(ec);
}

/// Builds the harness hello message using the representative test token.
std::string HelloMessage() {
    return R"({"messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("clientId": "client-1", "auth": {"method": "one_time_local_token", "token": ")" +
           std::string(kValidHexToken) + R"("}}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a subscription request for one authenticated session.
std::string SubscribeMessage(const std::string& sessionId, const std::string& messageId) {
    return R"({"messageType": "subscribe", "messageId": ")" + messageId + R"(", "sessionId": ")" + sessionId +
           R"(", "correlationId": null, "payload": {"stateAreas": ["character"]}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a character snapshot request for one authenticated session.
std::string SnapshotRequestMessage(const std::string& sessionId, const std::string& messageId) {
    return R"({"messageType": "snapshot_request", "messageId": ")" + messageId + R"(", "sessionId": ")" + sessionId +
           R"(", "correlationId": null, "payload": {"stateArea": "character"}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Decodes and returns the character level from a state snapshot envelope.
std::int64_t SnapshotLevel(const dovahlink::protocol::Envelope& snapshot) {
    auto decoded = dovahlink::protocol::DecodeStateSnapshotPayload(snapshot.payload);
    REQUIRE(decoded.has_value());
    auto characterState = dovahlink::protocol::DecodeCharacterState(decoded->data);
    REQUIRE(characterState.has_value());
    REQUIRE(characterState->level.has_value());
    return *characterState->level;
}

/// Asserts that a state snapshot envelope reports the character level as unavailable.
void CheckSnapshotLevelUnavailable(const dovahlink::protocol::Envelope& snapshot) {
    auto decoded = dovahlink::protocol::DecodeStateSnapshotPayload(snapshot.payload);
    REQUIRE(decoded.has_value());
    auto characterState = dovahlink::protocol::DecodeCharacterState(decoded->data);
    REQUIRE(characterState.has_value());
    CHECK_FALSE(characterState->level.has_value());
}

/// Reads and validates the harness's `BRIDGE_INSTANCE <id>` startup line,
/// returning the identifier text after the prefix.
std::string ReadBridgeInstanceId(HarnessProcess& harness) {
    constexpr std::string_view kPrefix = "BRIDGE_INSTANCE ";
    std::string line = harness.ReadLine();
    REQUIRE(line.starts_with(kPrefix));
    return line.substr(kPrefix.size());
}

/// Reads and validates one `PLAY_CONTEXT <id-or-(none)>` acknowledgment line,
/// returning the text after the prefix.
std::string ReadPlayContext(HarnessProcess& harness) {
    constexpr std::string_view kPrefix = "PLAY_CONTEXT ";
    std::string line = harness.ReadLine();
    REQUIRE(line.starts_with(kPrefix));
    return line.substr(kPrefix.size());
}

}  // namespace

TEST_CASE("dovahlink_bridge_harness serves one full session over a real socket and shuts down cleanly",
          "[harness]") {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    REQUIRE(harness.ReadLine() == "READY");
    std::string bridgeInstanceId = ReadBridgeInstanceId(harness);
    CHECK_FALSE(bridgeInstanceId.empty());
    CHECK(bridgeInstanceId != "(unavailable)");

    // A capture has nowhere to be attributed to before a play context exists
    // (ActivePlayContextLevelSink drops it, matching real play: main menu
    // has no play context). Begin one before subscribing, the same way a
    // real client would only see character state once a game is loaded.
    harness.WriteLine("new_game");
    CHECK_FALSE(ReadPlayContext(harness) == "(none)");

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    // The harness's own accept-loop thread starts before it prints READY
    // (Coordinator::Start() runs first), but binding a real OS port can
    // still lag a moment behind that; retry briefly rather than requiring
    // the first attempt to land.
    for (int attempt = 0; attempt < 20; ++attempt) {
        clientSocket.connect(boost::asio::ip::tcp::endpoint(boost::asio::ip::make_address("127.0.0.1"),
                                                              dovahlink::application::kBridgePort),
                              connectEc);
        if (!connectEc) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage());
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    REQUIRE(helloAck.sessionId.has_value());
    std::string sessionId = *helloAck.sessionId;

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    ClientWriteText(clientWs, SubscribeMessage(sessionId, "message-sub-1"));
    auto subscriptionAck = ClientReadEnvelope(clientWs);
    CHECK(subscriptionAck.messageType == "subscription_ack");
    auto initialSnapshot = ClientReadEnvelope(clientWs);
    CHECK(initialSnapshot.messageType == "state_snapshot");
    // Nothing has captured a level into this context yet: an unavailable
    // value, not a plausible default (protocol/schema/README.md).
    CheckSnapshotLevelUnavailable(initialSnapshot);

    harness.WriteLine("increase_level");
    CHECK(harness.ReadLine() == "LEVEL 6");

    // An unrecognized command must not crash or wedge the harness -- proven
    // by the rest of the scenario still completing normally afterward.
    harness.WriteLine("not_a_real_command");

    // Per bridge/README.md's documented Phase 1 boundary, the level change
    // is not pushed -- it only becomes visible by asking again.
    ClientWriteText(clientWs, SnapshotRequestMessage(sessionId, "message-snap-1"));
    auto recoverySnapshot = ClientReadEnvelope(clientWs);
    CHECK(recoverySnapshot.messageType == "state_snapshot");
    CHECK(SnapshotLevel(recoverySnapshot) == 6);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness reports a different bridge instance ID across a relaunch",
          "[harness]") {
    // Proves the harness's own documented purpose for this ID (see its
    // startup comment): a fresh identity per process launch. The .NET
    // RestartScenarioTests.cs proves the same property at the process
    // boundary the real acceptance test cares about; this case only proves
    // the harness-side generation itself varies, without needing a socket.
    std::string firstId;
    {
        HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
        REQUIRE(harness.ReadLine() == "READY");
        firstId = ReadBridgeInstanceId(harness);
        harness.WriteLine("quit");
        REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    }

    std::string secondId;
    {
        HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
        REQUIRE(harness.ReadLine() == "READY");
        secondId = ReadBridgeInstanceId(harness);
        harness.WriteLine("quit");
        REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    }

    CHECK(firstId != secondId);
}

TEST_CASE("dovahlink_bridge_harness's revoke command reports REVOKED for the given clientId",
          "[harness]") {
    // TrustStore::Revoke (bridge/security/trust_store.cpp) is intentionally idempotent: it
    // reports success for a clientId that was never trusted the same way it does for one that
    // actually held a credential, so REVOKED is the only branch reachable without a persisted
    // credential in the store. The .NET validator's PairingScenarioTests.cs
    // (integration/DovahLinkValidationClient.Tests) additionally proves that a real prior
    // credential stops authenticating after this command runs, using the isolated-trust-store and
    // full pairing-round-trip machinery this harness has no equivalent of on its own.
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    REQUIRE(harness.ReadLine() == "READY");
    (void)ReadBridgeInstanceId(harness);

    harness.WriteLine("revoke never-paired-client");
    CHECK(harness.ReadLine() == "REVOKED never-paired-client");

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's revoke command handles an empty clientId and is idempotent",
          "[harness]") {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    REQUIRE(harness.ReadLine() == "READY");
    (void)ReadBridgeInstanceId(harness);

    // Nothing after the "revoke " prefix: the substr parse yields an empty clientId rather than
    // failing to match the "revoke " branch at all.
    harness.WriteLine("revoke ");
    CHECK(harness.ReadLine() == "REVOKED ");

    // Revoking the same never-trusted clientId twice in one session stays REVOKED both times,
    // matching TrustStore::Revoke's documented idempotency.
    harness.WriteLine("revoke never-paired-client");
    CHECK(harness.ReadLine() == "REVOKED never-paired-client");
    harness.WriteLine("revoke never-paired-client");
    CHECK(harness.ReadLine() == "REVOKED never-paired-client");

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's new_game, load_game, and revert commands drive a real play-context "
          "lifecycle",
          "[harness]") {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    REQUIRE(harness.ReadLine() == "READY");
    (void)ReadBridgeInstanceId(harness);

    // No context before any load, matching GameLifecycleTracker's fresh
    // kNoContext state; revert is idempotent there.
    harness.WriteLine("revert");
    CHECK(ReadPlayContext(harness) == "(none)");

    harness.WriteLine("new_game");
    std::string newGameContext = ReadPlayContext(harness);
    CHECK_FALSE(newGameContext.empty());
    CHECK(newGameContext != "(none)");

    // A second new_game with no intervening revert still invalidates the
    // first context and mints a distinct one (GameLifecycleTracker's own
    // "invalidate-if-active" rule for kNewGame).
    harness.WriteLine("new_game");
    std::string secondNewGameContext = ReadPlayContext(harness);
    CHECK(secondNewGameContext != newGameContext);

    // Loading (even conceptually "the same save" -- this harness has no save
    // identity at all) mints a distinct context from the one just replaced,
    // the single most important assertion this design makes
    // (task.md's manual verification results).
    harness.WriteLine("load_game");
    std::string firstLoadContext = ReadPlayContext(harness);
    CHECK_FALSE(firstLoadContext.empty());
    CHECK(firstLoadContext != secondNewGameContext);

    harness.WriteLine("load_game");
    std::string secondLoadContext = ReadPlayContext(harness);
    CHECK(secondLoadContext != firstLoadContext);

    // A failed load still invalidates the current context (kPreLoadGame) but
    // settles into kNoContext rather than reviving or replacing it.
    harness.WriteLine("load_game_fail");
    CHECK(ReadPlayContext(harness) == "(none)");

    harness.WriteLine("revert");
    CHECK(ReadPlayContext(harness) == "(none)");

    // A fresh context after revert must not resurrect the reverted one.
    harness.WriteLine("new_game");
    CHECK(ReadPlayContext(harness) != secondLoadContext);

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness exits without printing READY when the token is missing",
          "[harness]") {
    HarnessProcess harness(kHarnessExePath, std::nullopt);

    CHECK(harness.ReadLine() != "READY");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() != 0);
}
