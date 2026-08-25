#include "application/bridge_config.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "protocol/messages.hpp"
#include "security/test_token.hpp"

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
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

// Launches the harness as a real process, drives its protocol over a real
// loopback socket, changes level through stdin, and verifies clean shutdown.

using dovahlink::security::kValidHexToken;

namespace {

constexpr const char *kHarnessExePath = DOVAHLINK_HARNESS_EXE;

// Builds an ANSI environment block from the test process environment, with
// DOVAHLINK_BRIDGE_TOKEN replaced by
// `tokenValue` (or omitted entirely if nullopt, to test the missing-token
// path) rather than left set globally -- Catch2 runs test cases sequentially
// in one process, so mutating this process's own environment would leak
// between tests. Also always forces DOVAHLINK_HARNESS_PORT_OVERRIDE=0, so
// every harness this file spawns binds its own OS-assigned port instead of
// the real, documented kBridgePort -- no test in this file ever contends for
// that one shared port.
/// Creates a unique path for one harness process's isolated trust store.
std::filesystem::path MakeTemporaryTrustStorePath() {
  char tempDirectory[MAX_PATH]{};
  DWORD directoryLength = GetTempPathA(MAX_PATH, tempDirectory);
  REQUIRE(directoryLength != 0);
  REQUIRE(directoryLength < MAX_PATH);

  char tempFile[MAX_PATH]{};
  REQUIRE(GetTempFileNameA(tempDirectory, "dvl", 0, tempFile) != 0);
  REQUIRE(DeleteFileA(tempFile));
  return std::filesystem::path(tempFile);
}

/// Builds the complete environment block used by the child process.
std::string
BuildEnvironmentBlock(std::optional<std::string> tokenValue,
                      std::optional<std::filesystem::path> trustStorePath) {
  std::string block;
  LPCH currentEnv = GetEnvironmentStringsA();
  REQUIRE(currentEnv != nullptr);
  std::string tokenPrefix =
      std::string(dovahlink::application::kTokenEnvVar) + "=";
  std::string portOverridePrefix = "DOVAHLINK_HARNESS_PORT_OVERRIDE=";
  std::string trustStoreOverridePrefix =
      "DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE=";
  for (LPCH entry = currentEnv; *entry != '\0';
       entry += std::strlen(entry) + 1) {
    std::string_view line(entry);
    if (!line.starts_with(tokenPrefix) &&
        !line.starts_with(portOverridePrefix) &&
        !line.starts_with(trustStoreOverridePrefix)) {
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
  block.append(portOverridePrefix);
  block.append("0");
  block.push_back('\0');
  if (trustStorePath.has_value()) {
    block.append(trustStoreOverridePrefix);
    block.append(trustStorePath->string());
    block.push_back('\0');
  }
  block.push_back('\0');
  return block;
}

/// Owns a harness subprocess and its redirected standard handles.
class HarnessProcess {
public:
  /// Launches the executable with the supplied token and optional trust-store
  /// environment values.
  HarnessProcess(
      const std::string &exePath, std::optional<std::string> tokenValue,
      std::optional<std::filesystem::path> trustStorePath = std::nullopt)
      : trustStorePath_(trustStorePath.value_or(MakeTemporaryTrustStorePath())),
        ownsTrustStorePath_(!trustStorePath.has_value()) {
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
    std::string environmentBlock =
        BuildEnvironmentBlock(std::move(tokenValue), trustStorePath_);
    BOOL started = CreateProcessA(nullptr, commandLine.data(), nullptr, nullptr,
                                  /*bInheritHandles=*/TRUE, 0,
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
    if (ownsTrustStorePath_) {
      std::error_code cleanupError;
      std::filesystem::remove(trustStorePath_, cleanupError);
      std::filesystem::path temporaryPath = trustStorePath_;
      temporaryPath += L".tmp";
      std::filesystem::remove(temporaryPath, cleanupError);
    }
  }

  /// Prevents copying process handles.
  HarnessProcess(const HarnessProcess &) = delete;
  /// Prevents assigning process handles.
  HarnessProcess &operator=(const HarnessProcess &) = delete;

  /// Returns the trust-store path supplied to the child process.
  [[nodiscard]] const std::filesystem::path &TrustStorePath() const {
    return trustStorePath_;
  }

  /// Writes one newline-terminated command to the child stdin.
  void WriteLine(const std::string &line) {
    std::string withNewline = line + "\n";
    DWORD written = 0;
    REQUIRE(WriteFile(stdinWrite_, withNewline.data(),
                      static_cast<DWORD>(withNewline.size()), &written,
                      nullptr));
  }

  /// Reads one line from child stdout, returning empty text on pipe closure.
  std::string ReadLine() {
    std::string result;
    char ch = '\0';
    DWORD bytesRead = 0;
    while (ReadFile(stdoutRead_, &ch, 1, &bytesRead, nullptr) &&
           bytesRead == 1) {
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
    auto result =
        WaitForSingleObject(process_, static_cast<DWORD>(timeout.count()));
    return result == WAIT_OBJECT_0;
  }

  /// Returns the child exit code after it has exited.
  [[nodiscard]] int ExitCode() const {
    DWORD exitCode = 0;
    REQUIRE(GetExitCodeProcess(process_, &exitCode));
    return static_cast<int>(exitCode);
  }

private:
  /// Trust-store path supplied to the child process.
  std::filesystem::path trustStorePath_;
  /// Whether this process owns and removes its automatically-created
  /// trust-store path.
  bool ownsTrustStorePath_ = false;
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
dovahlink::protocol::Envelope ClientReadEnvelope(
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> &ws) {
  boost::beast::flat_buffer buffer;
  boost::system::error_code ec;
  ws.read(buffer, ec);
  REQUIRE_FALSE(ec);
  auto parsed = dovahlink::protocol::ParseBoundedJson(
      boost::beast::buffers_to_string(buffer.data()));
  REQUIRE(parsed.has_value());
  auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
  REQUIRE(envelope.has_value());
  return std::move(*envelope);
}

/// Writes one text protocol message to the client socket.
void ClientWriteText(
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> &ws,
    const std::string &text) {
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
         std::string(kValidHexToken) +
         R"("}}, )"
         R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a subscription request for one authenticated session.
std::string SubscribeMessage(const std::string &sessionId,
                             const std::string &messageId) {
  return R"({"messageType": "subscribe", "messageId": ")" + messageId +
         R"(", "sessionId": ")" + sessionId +
         R"(", "correlationId": null, "payload": {"stateAreas": ["character"]}, )"
         R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Reads and validates the harness's `BRIDGE_INSTANCE <id>` startup line,
/// returning the identifier text after the prefix.
std::string ReadBridgeInstanceId(HarnessProcess &harness) {
  constexpr std::string_view kPrefix = "BRIDGE_INSTANCE ";
  std::string line = harness.ReadLine();
  REQUIRE(line.starts_with(kPrefix));
  return line.substr(kPrefix.size());
}

/// Reads and validates the harness's `PORT <n>` startup line, returning the
/// port it actually bound.
std::uint16_t ReadHarnessPort(HarnessProcess &harness) {
  constexpr std::string_view kPrefix = "PORT ";
  std::string line = harness.ReadLine();
  REQUIRE(line.starts_with(kPrefix));
  return static_cast<std::uint16_t>(std::stoi(line.substr(kPrefix.size())));
}

/// Reads and validates one `PLAY_CONTEXT <id-or-(none)>` acknowledgment line,
/// returning the text after the prefix.
std::string ReadPlayContext(HarnessProcess &harness) {
  constexpr std::string_view kPrefix = "PLAY_CONTEXT ";
  std::string line = harness.ReadLine();
  REQUIRE(line.starts_with(kPrefix));
  return line.substr(kPrefix.size());
}

} // namespace

TEST_CASE("dovahlink_bridge_harness serves one full session over a real socket "
          "and shuts down cleanly",
          "[harness]") {
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  std::string bridgeInstanceId = ReadBridgeInstanceId(harness);
  CHECK_FALSE(bridgeInstanceId.empty());
  CHECK(bridgeInstanceId != "(unavailable)");
  // BuildEnvironmentBlock always forces DOVAHLINK_HARNESS_PORT_OVERRIDE=0, so
  // this is an OS-assigned port private to this one harness instance, not the
  // shared kBridgePort.
  std::uint16_t port = ReadHarnessPort(harness);

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
    clientSocket.connect(boost::asio::ip::tcp::endpoint(
                             boost::asio::ip::make_address("127.0.0.1"), port),
                         connectEc);
    if (!connectEc) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
  }
  REQUIRE_FALSE(connectEc);

  // Proves the IPv6 listener actually resolved to the same port number reported
  // above, not just that the process started successfully.
  boost::asio::ip::tcp::socket v6ProbeSocket(ioc);
  boost::system::error_code v6ConnectEc;
  v6ProbeSocket.connect(boost::asio::ip::tcp::endpoint(
                            boost::asio::ip::make_address("::1"), port),
                        v6ConnectEc);
  CHECK_FALSE(v6ConnectEc);
  boost::system::error_code v6CloseEc;
  v6ProbeSocket.close(v6CloseEc);

  boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
      std::move(clientSocket));
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

  // No state area is currently registered (protocol/schema/README.md's
  // "Registered state areas"), so subscribe rejects the requested area and no
  // snapshot follows.
  ClientWriteText(clientWs, SubscribeMessage(sessionId, "message-sub-1"));
  auto subscriptionAck = ClientReadEnvelope(clientWs);
  CHECK(subscriptionAck.messageType == "subscription_ack");
  auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
      subscriptionAck.payload);
  REQUIRE(ack.has_value());
  CHECK(ack->acceptedStateAreas.empty());
  CHECK(ack->rejectedStateAreas == std::vector<std::string>{"character"});

  // The level-capture pipeline itself is proven independently of wire delivery:
  // the harness still captures a real value and reports it over stdout.
  harness.WriteLine("increase_level");
  CHECK(harness.ReadLine() == "LEVEL 6");

  // An unrecognized command must not crash or wedge the harness -- proven
  // by the rest of the scenario still completing normally afterward.
  harness.WriteLine("not_a_real_command");

  boost::system::error_code closeEc;
  clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

  harness.WriteLine("quit");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() == 0);
}

TEST_CASE("two harnesses alive at the same time bind distinct ports",
          "[harness]") {
  // Proves the actual claim this override exists for: two harness processes
  // running concurrently -- not sequentially, like the "fresh identity" test
  // above -- never contend for the same port.
  HarnessProcess first(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(first.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(first);
  std::uint16_t firstPort = ReadHarnessPort(first);

  HarnessProcess second(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(second.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(second);
  std::uint16_t secondPort = ReadHarnessPort(second);

  CHECK(firstPort != secondPort);
  CHECK(first.TrustStorePath() != second.TrustStorePath());

  first.WriteLine("quit");
  second.WriteLine("quit");
  REQUIRE(first.WaitForExit(std::chrono::seconds(5)));
  REQUIRE(second.WaitForExit(std::chrono::seconds(5)));
}

TEST_CASE("dovahlink_bridge_harness reports a different bridge instance ID "
          "across a relaunch",
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
    (void)ReadHarnessPort(harness);
    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  }

  std::string secondId;
  {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    REQUIRE(harness.ReadLine() == "READY");
    secondId = ReadBridgeInstanceId(harness);
    (void)ReadHarnessPort(harness);
    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  }

  CHECK(firstId != secondId);
}

TEST_CASE("dovahlink_bridge_harness's revoke command reports REVOKED for the "
          "given clientId",
          "[harness]") {
  // TrustStore::Revoke (bridge/security/trust_store.cpp) is intentionally
  // idempotent: it reports success for a clientId that was never trusted the
  // same way it does for one that actually held a credential, so REVOKED is the
  // only branch reachable without a persisted credential in the store. The .NET
  // validator's PairingScenarioTests.cs
  // (integration/DovahLinkValidationClient.Tests) additionally proves that a
  // real prior credential stops authenticating after this command runs, using
  // the isolated-trust-store and full pairing-round-trip machinery this harness
  // has no equivalent of on its own.
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(harness);
  (void)ReadHarnessPort(harness);

  harness.WriteLine("revoke never-paired-client");
  CHECK(harness.ReadLine() == "REVOKED never-paired-client");

  harness.WriteLine("quit");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's revoke command handles an empty clientId "
          "and is idempotent",
          "[harness]") {
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(harness);
  (void)ReadHarnessPort(harness);

  // Nothing after the "revoke " prefix: the substr parse yields an empty
  // clientId rather than failing to match the "revoke " branch at all.
  harness.WriteLine("revoke ");
  CHECK(harness.ReadLine() == "REVOKED ");

  // Revoking the same never-trusted clientId twice in one session stays REVOKED
  // both times, matching TrustStore::Revoke's documented idempotency.
  harness.WriteLine("revoke never-paired-client");
  CHECK(harness.ReadLine() == "REVOKED never-paired-client");
  harness.WriteLine("revoke never-paired-client");
  CHECK(harness.ReadLine() == "REVOKED never-paired-client");

  harness.WriteLine("quit");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's block command reports BLOCK_FAILED for a "
          "never-paired clientId",
          "[harness]") {
  // Unlike TrustStore::Revoke, TrustStore::Block is not idempotent-success for
  // an unknown clientId (bridge/security/trust_store.cpp): blocking targets an
  // existing Known Device record, not a bare identity string, so a never-paired
  // clientId reports kNotFound and this command reports BLOCK_FAILED. The .NET
  // validator's PairingScenarioTests.cs additionally proves the real
  // block-while-connected/reconnect-rejected round trip against an actually
  // trusted device, using machinery this harness test file has no equivalent of
  // on its own (matching the existing revoke tests' own documented scope
  // split).
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(harness);
  (void)ReadHarnessPort(harness);

  // An empty clientId (nothing after the "block " prefix) still matches the
  // branch and reports failure the same way, mirroring the equivalent "revoke "
  // test.
  harness.WriteLine("block ");
  CHECK(harness.ReadLine() == "BLOCK_FAILED ");

  // Repeating the same never-paired clientId stays BLOCK_FAILED every time:
  // kNotFound is consistently reported, not just on the first attempt.
  harness.WriteLine("block never-paired-client");
  CHECK(harness.ReadLine() == "BLOCK_FAILED never-paired-client");
  harness.WriteLine("block never-paired-client");
  CHECK(harness.ReadLine() == "BLOCK_FAILED never-paired-client");

  harness.WriteLine("quit");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's unblock command reports UNBLOCK_FAILED "
          "for a never-paired clientId",
          "[harness]") {
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(harness);
  (void)ReadHarnessPort(harness);

  harness.WriteLine("unblock ");
  CHECK(harness.ReadLine() == "UNBLOCK_FAILED ");

  harness.WriteLine("unblock never-paired-client");
  CHECK(harness.ReadLine() == "UNBLOCK_FAILED never-paired-client");
  harness.WriteLine("unblock never-paired-client");
  CHECK(harness.ReadLine() == "UNBLOCK_FAILED never-paired-client");

  harness.WriteLine("quit");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() == 0);
}

TEST_CASE("dovahlink_bridge_harness's trust_reset command reports TRUST_RESET "
          "for the given clientId",
          "[harness]") {
  // Like TrustStore::Revoke, TrustStore::ResetTrust
  // (bridge/security/trust_store.cpp) is idempotent-success: it revokes
  // whichever currently-trusted devices exist (none, with no prior pairing) and
  // still reports success, so TRUST_RESET is the only branch reachable without
  // a persistence-layer save failure to inject. The .NET validator's
  // PairingScenarioTests.cs additionally proves the real
  // trust-reset-while-connected round trip against an actually trusted device,
  // using machinery this harness test file has no equivalent of on its own
  // (matching the existing revoke tests' own documented scope split).
  std::filesystem::path trustStorePath;
  {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    trustStorePath = harness.TrustStorePath();
    REQUIRE(harness.ReadLine() == "READY");
    (void)ReadBridgeInstanceId(harness);
    (void)ReadHarnessPort(harness);

    // An empty clientId (nothing after the "trust_reset " prefix) still matches
    // the branch and reports success the same way, mirroring the equivalent
    // "revoke " test.
    harness.WriteLine("trust_reset ");
    CHECK(harness.ReadLine() == "TRUST_RESET ");
    CHECK(std::filesystem::exists(trustStorePath));

    // Repeating the same never-trusted clientId stays TRUST_RESET every time,
    // matching ResetTrust's documented idempotency.
    harness.WriteLine("trust_reset never-paired-client");
    CHECK(harness.ReadLine() == "TRUST_RESET never-paired-client");
    harness.WriteLine("trust_reset never-paired-client");
    CHECK(harness.ReadLine() == "TRUST_RESET never-paired-client");

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
  }
  CHECK_FALSE(std::filesystem::exists(trustStorePath));
}

TEST_CASE("dovahlink_bridge_harness's factory_reset command reports "
          "FACTORY_RESET and is idempotent",
          "[harness]") {
  // Like TrustStore::Reset's ResetTrust sibling above, TrustStore::Reset is
  // idempotent-success: wiping an already-empty trust store still reports
  // success, so FACTORY_RESET is the only branch reachable without a
  // persistence-layer save failure to inject. Unlike every other trust command,
  // factory_reset takes no clientId -- it wipes every known device
  // unconditionally. The .NET validator's PairingScenarioTests.cs additionally
  // proves the real factory-reset-while-connected round trip against an
  // actually trusted device.
  std::filesystem::path trustStorePath;
  {
    HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
    trustStorePath = harness.TrustStorePath();
    REQUIRE(harness.ReadLine() == "READY");
    (void)ReadBridgeInstanceId(harness);
    (void)ReadHarnessPort(harness);

    harness.WriteLine("factory_reset");
    CHECK(harness.ReadLine() == "FACTORY_RESET");
    CHECK(std::filesystem::exists(trustStorePath));
    harness.WriteLine("factory_reset");
    CHECK(harness.ReadLine() == "FACTORY_RESET");

    harness.WriteLine("quit");
    REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
    CHECK(harness.ExitCode() == 0);
  }
  CHECK_FALSE(std::filesystem::exists(trustStorePath));
}

TEST_CASE("dovahlink_bridge_harness's new_game, load_game, and revert commands "
          "drive a real play-context "
          "lifecycle",
          "[harness]") {
  HarnessProcess harness(kHarnessExePath, std::string(kValidHexToken));
  REQUIRE(harness.ReadLine() == "READY");
  (void)ReadBridgeInstanceId(harness);
  (void)ReadHarnessPort(harness);

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

TEST_CASE("dovahlink_bridge_harness exits without printing READY when the "
          "token is missing",
          "[harness]") {
  HarnessProcess harness(kHarnessExePath, std::nullopt);

  CHECK(harness.ReadLine() != "READY");
  REQUIRE(harness.WaitForExit(std::chrono::seconds(5)));
  CHECK(harness.ExitCode() != 0);
}
