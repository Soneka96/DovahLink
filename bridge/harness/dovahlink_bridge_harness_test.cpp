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
    return R"({"protocolVersion": 0, "messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("supportedProtocolVersions": [1], "auth": {"method": "one_time_local_token", "token": ")" +
           std::string(kValidHexToken) + R"("}}})";
}

/// Builds a subscription request for one authenticated session.
std::string SubscribeMessage(const std::string& sessionId, const std::string& messageId) {
    return R"({"protocolVersion": 1, "messageType": "subscribe", "messageId": ")" + messageId +
           R"(", "sessionId": ")" + sessionId + R"(", "correlationId": null, "payload": {"stateAreas": )"
           R"(["character"]}})";
}

/// Builds a character snapshot request for one authenticated session.
std::string SnapshotRequestMessage(const std::string& sessionId, const std::string& messageId) {
    return R"({"protocolVersion": 1, "messageType": "snapshot_request", "messageId": ")" + messageId +
           R"(", "sessionId": ")" + sessionId + R"(", "correlationId": null, "payload": {"stateArea": )"
           R"("character"}})";
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

/// Reads and validates the harness's `BRIDGE_INSTANCE <id>` startup line,
/// returning the identifier text after the prefix.
std::string ReadBridgeInstanceId(HarnessProcess& harness) {
    constexpr std::string_view kPrefix = "BRIDGE_INSTANCE ";
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
    CHECK(SnapshotLevel(initialSnapshot) == 5);

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

TEST_CASE("dovahlink_bridge_harness exits without printing READY when the token is missing",
          "[harness]") {
    HarnessProcess harness(kHarnessExePath, std::nullopt);

    CHECK(harness.ReadLine() != "READY");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() != 0);
}
