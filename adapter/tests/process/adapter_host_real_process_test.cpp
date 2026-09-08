#include "capture/adapter_capture_handoff_queue.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "identity/adapter_instance_id_generator.hpp"
#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_session.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/settable_adapter_ipc_peer_proof_provider.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "process/adapter_host_endpoint.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_host_supervisor.hpp"
#include "process/adapter_owner_lifetime_id.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <functional>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
#include <optional>
#include <regex>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <tuple>
#include <utility>
#include <vector>

using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::dispatch::AdapterNativeDispatcher;
using dovahlink::adapter::identity::AdapterInstanceIdGenerator;
using dovahlink::adapter::ipc::AdapterIpcConnection;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::AdapterIpcTarget;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::SettableAdapterIpcPeerProofProvider;
using dovahlink::adapter::ipc::TrustAdminListScope;
using dovahlink::adapter::ipc::TrustAdminOperation;
using dovahlink::adapter::ipc::TrustAdminRequestOutcome;
using dovahlink::adapter::ipc::TrustAdminRequestResult;
using dovahlink::adapter::ipc::WinsockAdapterIpcSocket;
using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::AdapterHostSupervisor;
using dovahlink::adapter::process::DeriveOwnerLifetimeId;
using dovahlink::adapter::process::FileAdapterHostRendezvousReader;
using dovahlink::adapter::process::ResolveDefaultRendezvousFilePath;
using dovahlink::adapter::process::Win32AdapterHostProcessLauncher;
using dovahlink::adapter::process::WindowsEventAdapterHostShutdownRequester;
using dovahlink::adapter::runtime::IAdapterTaskMarshaller;

namespace {

///  Removes a test's per-lifetime rendezvous file even when an assertion
///  aborts the test body after the real host has been launched.
class ScopedRendezvousCleanup {
public:
  explicit ScopedRendezvousCleanup(std::filesystem::path path)
      : path_(std::move(path)) {}

  ~ScopedRendezvousCleanup() {
    std::error_code error;
    std::filesystem::remove(path_, error);
  }

  ScopedRendezvousCleanup(const ScopedRendezvousCleanup &) = delete;
  ScopedRendezvousCleanup &operator=(const ScopedRendezvousCleanup &) = delete;

private:
  ///  The per-test rendezvous file to remove.
  std::filesystem::path path_;
};

///  A small owner-process controller used to prove that the real host is
///  terminated when the adapter process holding its Job Object disappears.
class OwnerFixtureProcess {
public:
  explicit OwnerFixtureProcess(std::filesystem::path executablePath) {
    SECURITY_ATTRIBUTES attributes{};
    attributes.nLength = sizeof(attributes);
    attributes.bInheritHandle = TRUE;

    HANDLE pipeWrite = nullptr;
    if (!CreatePipe(&pipeRead_, &pipeWrite, &attributes, 0) ||
        !SetHandleInformation(pipeRead_, HANDLE_FLAG_INHERIT, 0)) {
      if (pipeWrite != nullptr) {
        CloseHandle(pipeWrite);
      }
      throw std::runtime_error("Unable to create the owner fixture pipe.");
    }

    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    startupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
    startupInfo.wShowWindow = SW_HIDE;
    startupInfo.hStdOutput = pipeWrite;
    startupInfo.hStdError = pipeWrite;

    std::wstring commandLine = L"\"" + executablePath.native() + L"\"";
    PROCESS_INFORMATION processInfo{};
    BOOL created = CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr,
                                  /*bInheritHandles=*/TRUE, CREATE_NO_WINDOW,
                                  nullptr, nullptr, &startupInfo, &processInfo);
    CloseHandle(pipeWrite);
    if (!created) {
      CloseHandle(pipeRead_);
      pipeRead_ = nullptr;
      throw std::runtime_error("Unable to launch the owner fixture.");
    }

    process_ = processInfo.hProcess;
    thread_ = processInfo.hThread;
  }

  ~OwnerFixtureProcess() {
    Terminate();
    if (thread_ != nullptr) {
      CloseHandle(thread_);
    }
    if (process_ != nullptr) {
      CloseHandle(process_);
    }
    if (pipeRead_ != nullptr) {
      CloseHandle(pipeRead_);
    }
  }

  OwnerFixtureProcess(const OwnerFixtureProcess &) = delete;
  OwnerFixtureProcess &operator=(const OwnerFixtureProcess &) = delete;

  ///  Reads the owner lifetime and host port reported by the fixture.
  std::optional<std::tuple<
      std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>,
      std::uint16_t, std::uint32_t>>
  ReadStartup(std::chrono::milliseconds timeout) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string buffer;
    while (std::chrono::steady_clock::now() < deadline) {
      DWORD available = 0;
      if (!PeekNamedPipe(pipeRead_, nullptr, 0, nullptr, &available, nullptr)) {
        return std::nullopt;
      }
      if (available == 0) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        continue;
      }

      std::array<char, 128> chunk{};
      DWORD bytesToRead =
          static_cast<DWORD>(std::min<std::size_t>(available, chunk.size()));
      DWORD bytesRead = 0;
      if (!ReadFile(pipeRead_, chunk.data(), bytesToRead, &bytesRead,
                    nullptr)) {
        return std::nullopt;
      }
      buffer.append(chunk.data(), bytesRead);

      const std::size_t firstNewline = buffer.find('\n');
      const std::size_t secondNewline =
          firstNewline == std::string::npos
              ? std::string::npos
              : buffer.find('\n', firstNewline + 1);
      const std::size_t thirdNewline =
          secondNewline == std::string::npos
              ? std::string::npos
              : buffer.find('\n', secondNewline + 1);
      if (thirdNewline == std::string::npos) {
        continue;
      }

      std::string ownerLine = buffer.substr(0, firstNewline);
      std::string portLine =
          buffer.substr(firstNewline + 1, secondNewline - firstNewline - 1);
      std::string hostPidLine =
          buffer.substr(secondNewline + 1, thirdNewline - secondNewline - 1);
      if (!ownerLine.empty() && ownerLine.back() == '\r') {
        ownerLine.pop_back();
      }
      if (!portLine.empty() && portLine.back() == '\r') {
        portLine.pop_back();
      }
      if (!hostPidLine.empty() && hostPidLine.back() == '\r') {
        hostPidLine.pop_back();
      }
      constexpr std::string_view ownerPrefix = "OWNER ";
      constexpr std::string_view portPrefix = "PORT ";
      constexpr std::string_view hostPidPrefix = "HOST_PID ";
      if (!ownerLine.starts_with(ownerPrefix) ||
          !portLine.starts_with(portPrefix) ||
          !hostPidLine.starts_with(hostPidPrefix)) {
        return std::nullopt;
      }

      auto ownerLifetimeId = dovahlink::adapter::process::ParseOwnerLifetimeId(
          ownerLine.substr(ownerPrefix.size()));
      int port = 0;
      const std::string portText = portLine.substr(portPrefix.size());
      const auto [end, error] = std::from_chars(
          portText.data(), portText.data() + portText.size(), port);
      std::uint32_t hostPid = 0;
      const std::string hostPidText = hostPidLine.substr(hostPidPrefix.size());
      const auto [hostPidEnd, hostPidError] = std::from_chars(
          hostPidText.data(), hostPidText.data() + hostPidText.size(), hostPid);
      if (!ownerLifetimeId.has_value() || error != std::errc{} ||
          end != portText.data() + portText.size() || port <= 0 ||
          port > 65535 || hostPidError != std::errc{} ||
          hostPidEnd != hostPidText.data() + hostPidText.size() ||
          hostPid == 0) {
        return std::nullopt;
      }
      return std::make_tuple(*ownerLifetimeId, static_cast<std::uint16_t>(port),
                             hostPid);
    }
    return std::nullopt;
  }

  ///  Terminates the owner process and waits for it to leave.
  bool Terminate() {
    if (process_ == nullptr ||
        WaitForSingleObject(process_, 0) == WAIT_OBJECT_0) {
      return true;
    }
    TerminateProcess(process_, 1);
    return WaitForSingleObject(process_, 5000) == WAIT_OBJECT_0;
  }

private:
  ///  The owner fixture's stdout pipe.
  HANDLE pipeRead_ = nullptr;
  ///  The owner fixture process handle.
  HANDLE process_ = nullptr;
  ///  The owner fixture's primary thread handle.
  HANDLE thread_ = nullptr;
};

///  Executes a game-thread task immediately for transport-only integration
///  tests. The real Skyrim marshaller is not involved in this process test.
class ImmediateTaskMarshaller final : public IAdapterTaskMarshaller {
public:
  void RunOnGameThread(std::function<void()> task) override { task(); }
};

///  Accepts captured values without starting another worker, since the
///  cross-process test is concerned with IPC connection recovery.
class NoopCaptureQueue final : public IAdapterCaptureHandoffQueue {
public:
  bool TryEnqueue(AdapterCaptureWorkItem) override { return true; }
  void Stop() override {}
};

///  Presents nothing, since this cross-process test is concerned with IPC
///  connection recovery, not pairing display.
class NoopPairingNotificationSink final
    : public dovahlink::adapter::ipc::IAdapterPairingNotificationSink {
public:
  bool Display(const std::string &,
               dovahlink::adapter::ipc::PairingDisplayMode) override {
    return true;
  }
  void NotifyAttemptsExhausted() override {}
};

///  Records every `Display` call for the real cross-language pairing-display
///  E2E test, accepting or rejecting per `SetAcceptDisplay`. Thread-safe:
///  `Display` runs on whichever thread `AdapterIpcSession` marshals
///  game-thread work from -- this fixture's `ImmediateTaskMarshaller` runs it
///  inline on the private IPC connection's own read thread, not this test's
///  main thread.
class RecordingPairingNotificationSink final
    : public dovahlink::adapter::ipc::IAdapterPairingNotificationSink {
public:
  bool Display(const std::string &code,
               dovahlink::adapter::ipc::PairingDisplayMode mode) override {
    std::lock_guard<std::mutex> lock(mutex_);
    displayed_.emplace_back(code, mode);
    return acceptDisplay_;
  }

  void NotifyAttemptsExhausted() override {}

  ///  Every `Display` call observed so far, in order.
  std::vector<
      std::pair<std::string, dovahlink::adapter::ipc::PairingDisplayMode>>
  Displayed() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return displayed_;
  }

  ///  Sets the value every subsequent `Display` call returns; `true` (accept
  ///  the display) until changed.
  void SetAcceptDisplay(bool accept) {
    std::lock_guard<std::mutex> lock(mutex_);
    acceptDisplay_ = accept;
  }

private:
  ///  Guards `displayed_` and `acceptDisplay_`.
  mutable std::mutex mutex_;
  ///  Every `Display` call observed so far, in order. Guarded by `mutex_`.
  std::vector<
      std::pair<std::string, dovahlink::adapter::ipc::PairingDisplayMode>>
      displayed_;
  ///  The value `Display` returns. Guarded by `mutex_`.
  bool acceptDisplay_ = true;
};

///  A minimal, test-only WebSocket client speaking just enough of RFC 6455
///  and the public protocol's plain-JSON envelope to drive a real Host's
///  public listener from this native test process: a raw TCP connect, the
///  HTTP/1.1 upgrade handshake
///  `PublicWebSocketHandshake::TryParseUpgradeRequest`
///  (`host/DovahLink.Host/Client/Transport/`) expects, and RFC 6455's
///  mandatory client-to-server frame masking. Does not verify the server's
///  returned `Sec-WebSocket-Accept` value: this test trusts its own real
///  Host's handshake response rather than re-implementing a general-purpose
///  WebSocket client. No production code depends on this class; it exists
///  only to prove the private IPC pairing-display wire agreement this file's
///  own tests otherwise cannot reach, by acting as a real external public
///  client for the one real Host process under test.
class MinimalPublicWebSocketClient {
public:
  ///  Connects to the given loopback port and completes the WebSocket
  ///  upgrade handshake.
  explicit MinimalPublicWebSocketClient(std::uint16_t port) {
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
      throw std::runtime_error("Unable to initialize Winsock.");
    }
    winsockStarted_ = true;

    socket_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (socket_ == INVALID_SOCKET) {
      throw std::runtime_error("Unable to create the public WebSocket client "
                               "socket.");
    }

    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(port);
    inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    if (connect(socket_, reinterpret_cast<const sockaddr *>(&address),
                sizeof(address)) == SOCKET_ERROR) {
      throw std::runtime_error(
          "Unable to connect to the real Host's public listener.");
    }

    //  Bounds every subsequent blocking recv() in ReadExact, so an
    //  unresponsive or slow real Host fails this client with a clear
    //  exception instead of hanging the whole test process indefinitely. No
    //  matching send timeout: every payload this client ever writes (the
    //  HTTP upgrade request and small JSON envelope frames) is far below the
    //  OS socket send buffer, so send() has no realistic path to blocking
    //  here.
    if (setsockopt(socket_, SOL_SOCKET, SO_RCVTIMEO,
                   reinterpret_cast<const char *>(&kReceiveTimeoutMilliseconds),
                   sizeof(kReceiveTimeoutMilliseconds)) == SOCKET_ERROR) {
      throw std::runtime_error(
          "Unable to set the public WebSocket client's receive timeout.");
    }

    const std::string request =
        "GET / HTTP/1.1\r\n"
        "Host: 127.0.0.1:" +
        std::to_string(port) +
        "\r\n"
        "Upgrade: websocket\r\n"
        "Connection: Upgrade\r\n"
        "Sec-WebSocket-Version: 13\r\n"
        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
        "\r\n";
    SendRaw(request.data(), request.size());

    std::string response = ReadUntilBlankLine();
    if (response.rfind("HTTP/1.1 101", 0) != 0) {
      throw std::runtime_error(
          "The real Host rejected the WebSocket upgrade handshake: " +
          response);
    }
  }

  ///  Closes the socket and releases this instance's Winsock reference.
  ~MinimalPublicWebSocketClient() {
    if (socket_ != INVALID_SOCKET) {
      closesocket(socket_);
    }
    if (winsockStarted_) {
      WSACleanup();
    }
  }

  MinimalPublicWebSocketClient(const MinimalPublicWebSocketClient &) = delete;
  MinimalPublicWebSocketClient &
  operator=(const MinimalPublicWebSocketClient &) = delete;

  ///  Sends `json` as one masked WebSocket text frame, per RFC 6455's
  ///  client-to-server framing.
  void SendText(std::string_view json) {
    std::vector<std::uint8_t> frame;
    frame.push_back(0x81); //  FIN + text opcode.
    const std::size_t length = json.size();
    if (length < 126) {
      frame.push_back(static_cast<std::uint8_t>(0x80 | length));
    } else if (length <= 0xFFFF) {
      frame.push_back(0x80 | 126);
      frame.push_back(static_cast<std::uint8_t>((length >> 8) & 0xFF));
      frame.push_back(static_cast<std::uint8_t>(length & 0xFF));
    } else {
      throw std::runtime_error(
          "MinimalPublicWebSocketClient does not support payloads this "
          "large.");
    }
    const std::array<std::uint8_t, 4> mask{0x12, 0x34, 0x56, 0x78};
    frame.insert(frame.end(), mask.begin(), mask.end());
    for (std::size_t i = 0; i < length; ++i) {
      frame.push_back(static_cast<std::uint8_t>(json[i]) ^ mask[i % 4]);
    }
    SendRaw(reinterpret_cast<const char *>(frame.data()), frame.size());
  }

  ///  Reads one complete, unfragmented, unmasked WebSocket text frame from
  ///  the server and returns its payload.
  std::string ReceiveText() {
    std::array<std::uint8_t, 2> header{};
    ReadExact(reinterpret_cast<char *>(header.data()), header.size());
    const std::uint8_t opcode = header[0] & 0x0F;
    if ((header[0] & 0x80) == 0) {
      throw std::runtime_error(
          "MinimalPublicWebSocketClient does not support fragmented "
          "frames.");
    }
    if (opcode != 0x1) {
      throw std::runtime_error("Expected a text frame from the real Host.");
    }
    if ((header[1] & 0x80) != 0) {
      throw std::runtime_error("A server-to-client frame must not be "
                               "masked.");
    }
    std::uint64_t length = header[1] & 0x7F;
    if (length == 126) {
      std::array<std::uint8_t, 2> extended{};
      ReadExact(reinterpret_cast<char *>(extended.data()), extended.size());
      length = (static_cast<std::uint64_t>(extended[0]) << 8) | extended[1];
    } else if (length == 127) {
      throw std::runtime_error(
          "MinimalPublicWebSocketClient does not support payloads this "
          "large.");
    }
    std::string payload(length, '\0');
    if (length > 0) {
      ReadExact(payload.data(), payload.size());
    }
    return payload;
  }

private:
  ///  Sends every byte in `[data, data + size)`, retrying a short write
  ///  until the whole buffer is sent.
  void SendRaw(const char *data, std::size_t size) {
    std::size_t sent = 0;
    while (sent < size) {
      int result = send(socket_, data + sent, static_cast<int>(size - sent), 0);
      if (result == SOCKET_ERROR) {
        throw std::runtime_error(
            "Unable to write to the real Host's public listener.");
      }
      sent += static_cast<std::size_t>(result);
    }
  }

  ///  Reads exactly `size` bytes into `data`, blocking until the whole
  ///  buffer is filled.
  void ReadExact(char *data, std::size_t size) {
    std::size_t received = 0;
    while (received < size) {
      int result =
          recv(socket_, data + received, static_cast<int>(size - received), 0);
      if (result <= 0) {
        throw std::runtime_error(
            "The real Host's public listener closed the connection early or "
            "did not respond within the receive timeout.");
      }
      received += static_cast<std::size_t>(result);
    }
  }

  ///  Reads one byte at a time until the terminating blank line of an HTTP
  ///  response is seen, returning everything read.
  std::string ReadUntilBlankLine() {
    std::string response;
    char byte;
    while (true) {
      ReadExact(&byte, 1);
      response.push_back(byte);
      if (response.size() >= 4 &&
          response.compare(response.size() - 4, 4, "\r\n\r\n") == 0) {
        return response;
      }
    }
  }

  ///  How long a single blocking recv() may wait before this client treats
  ///  an unresponsive or slow real Host as a failure instead of hanging the
  ///  test process indefinitely.
  static constexpr DWORD kReceiveTimeoutMilliseconds = 10000;

  ///  The connected socket.
  SOCKET socket_ = INVALID_SOCKET;
  ///  Whether this instance owns a Winsock startup reference.
  bool winsockStarted_ = false;
};

///  RAII guard that sets `DOVAHLINK_TEST_PUBLIC_LISTENER_PORT` for the scope
///  of a single test, then clears it -- so the real Host process this test
///  launches opens its public listener on a fixed port (per
///  `Program::ParseTestPublicListenerPort`), and no other test sharing this
///  process's environment block ever observes it set.
class ScopedTestPublicListenerPortEnvironmentVariable {
public:
  explicit ScopedTestPublicListenerPortEnvironmentVariable(std::uint16_t port) {
    if (_putenv_s("DOVAHLINK_TEST_PUBLIC_LISTENER_PORT",
                  std::to_string(port).c_str()) != 0) {
      throw std::runtime_error(
          "Unable to set the public-listener-port environment variable.");
    }
  }

  ~ScopedTestPublicListenerPortEnvironmentVariable() {
    _putenv_s("DOVAHLINK_TEST_PUBLIC_LISTENER_PORT", "");
  }

  ScopedTestPublicListenerPortEnvironmentVariable(
      const ScopedTestPublicListenerPortEnvironmentVariable &) = delete;
  ScopedTestPublicListenerPortEnvironmentVariable &
  operator=(const ScopedTestPublicListenerPortEnvironmentVariable &) = delete;
};

///  A real loopback listener that occupies a port without speaking the
///  private IPC protocol, representing stale rendezvous data naming an
///  unrelated process.
class ScopedLoopbackListener {
public:
  ///  Binds and listens on an operating-system-assigned loopback port.
  ScopedLoopbackListener() {
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
      throw std::runtime_error("Unable to initialize Winsock.");
    }
    winsockStarted_ = true;

    listener_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener_ == INVALID_SOCKET) {
      throw std::runtime_error("Unable to create stale listener socket.");
    }

    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(0);
    inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    if (bind(listener_, reinterpret_cast<const sockaddr *>(&address),
             sizeof(address)) == SOCKET_ERROR ||
        listen(listener_, 1) == SOCKET_ERROR) {
      throw std::runtime_error("Unable to bind stale listener socket.");
    }

    int addressLength = sizeof(address);
    if (getsockname(listener_, reinterpret_cast<sockaddr *>(&address),
                    &addressLength) == SOCKET_ERROR) {
      throw std::runtime_error("Unable to read stale listener port.");
    }
    port_ = ntohs(address.sin_port);
  }

  ///  Closes the occupied listener and releases its Winsock reference.
  ~ScopedLoopbackListener() {
    if (listener_ != INVALID_SOCKET) {
      closesocket(listener_);
    }
    if (winsockStarted_) {
      WSACleanup();
    }
  }

  ScopedLoopbackListener(const ScopedLoopbackListener &) = delete;
  ScopedLoopbackListener &operator=(const ScopedLoopbackListener &) = delete;

  ///  Returns the occupied loopback port.
  std::uint16_t Port() const { return port_; }

private:
  ///  The occupied listening socket.
  SOCKET listener_ = INVALID_SOCKET;
  ///  Whether this instance owns a Winsock startup reference.
  bool winsockStarted_ = false;
  ///  The operating-system-assigned listening port.
  std::uint16_t port_ = 0;
};

///  A test-only IPC connection that records the target startup selected by the
///  supervisor without opening a second real connection in this fallback test.
class RecordingAdapterIpcConnection final
    : public dovahlink::adapter::ipc::IAdapterIpcConnection {
public:
  ///  Records the target selected by the supervisor.
  void
  ConfigureTarget(dovahlink::adapter::ipc::AdapterIpcTarget target) override {
    std::lock_guard<std::mutex> lock(mutex_);
    target_ = std::move(target);
  }

  ///  Records a connection start request and invokes the one-shot test hook.
  void Start() override {
    std::function<void()> callback;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      ++startCount_;
      callback = std::move(onStart_);
    }
    if (callback) {
      callback();
    }
  }

  ///  Invokes a test-controlled callback after a connection start is recorded.
  void SetOnStartCallback(std::function<void()> callback) {
    std::lock_guard<std::mutex> lock(mutex_);
    onStart_ = std::move(callback);
  }

  ///  The fallback test never sends through this connection.
  bool TrySend(const IpcMessage &) override { return true; }

  ///  The fallback test has no connection worker to stop.
  void Stop() override {}

  ///  Returns how many times the supervisor requested startup.
  int StartCount() const { return startCount_.load(); }

  ///  Returns the selected target port, or zero when no target is configured.
  std::uint16_t ConfiguredPort() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->port : 0;
  }

  ///  Returns the selected target proof token, or an empty token.
  std::vector<std::byte> ConfiguredProofToken() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->proofToken : std::vector<std::byte>{};
  }

  ///  Returns the selected target HostProof key, or an empty key.
  std::vector<std::byte> ConfiguredHostProofKey() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->hostProofKey
                               : std::vector<std::byte>{};
  }

private:
  ///  Guards the selected target snapshot.
  mutable std::mutex mutex_;
  ///  The observed number of connection start requests.
  std::atomic<int> startCount_ = 0;
  ///  A one-shot callback invoked after a start is recorded.
  std::function<void()> onStart_;
  ///  The target snapshot most recently selected by the supervisor.
  std::optional<dovahlink::adapter::ipc::AdapterIpcTarget> target_;
};

///  Builds a deterministic lifetime identity for two concurrent test hosts.
std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
LifetimeIdWithMarker(std::byte marker) {
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes> id{};
  id.fill(marker);
  return id;
}

///  Extracts a top-level JSON string field's value by key from `json`, using
///  plain substring search rather than a full JSON parser -- sufficient for
///  this test's own real, well-formed server responses. Returns an empty
///  string if `key` is absent or its value is not a JSON string.
std::string ExtractJsonStringField(const std::string &json,
                                   const std::string &key) {
  const std::string marker = "\"" + key + "\":\"";
  std::size_t start = json.find(marker);
  if (start == std::string::npos) {
    return {};
  }
  start += marker.size();
  std::size_t end = json.find('"', start);
  if (end == std::string::npos) {
    return {};
  }
  return json.substr(start, end - start);
}

///  Waits for a bounded asynchronous process condition without busy spinning.
template <typename Predicate>
bool WaitUntil(Predicate predicate, std::chrono::milliseconds timeout) {
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return predicate();
}

///  Returns whether a real loopback port no longer accepts connections.
bool IsPortClosed(std::uint16_t port) {
  WinsockAdapterIpcSocket socket(port);
  const bool connected = socket.Connect();
  if (connected) {
    socket.Close();
  }
  return !connected;
}

///  Waits for a process id to become signaled after its owning process dies.
bool WaitForProcessExit(std::uint32_t processId) {
  HANDLE process = OpenProcess(SYNCHRONIZE, FALSE, processId);
  if (process == nullptr) {
    return GetLastError() == ERROR_INVALID_PARAMETER;
  }
  const bool exited = WaitForSingleObject(process, 5000) == WAIT_OBJECT_0;
  CloseHandle(process);
  return exited;
}

///  Checks whether a process id is still running, through an independent
///  SYNCHRONIZE handle -- unlike
///  `Win32AdapterHostProcessLauncher::AwaitExitOrTerminate`, which
///  force-terminates the process it checks once its timeout elapses, this
///  never affects the process's lifetime.
bool IsProcessStillRunning(std::uint32_t processId) {
  HANDLE process =
      OpenProcess(SYNCHRONIZE, /*bInheritHandle=*/FALSE, processId);
  if (process == nullptr) {
    return false;
  }
  const bool stillRunning = WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
  CloseHandle(process);
  return stillRunning;
}

} //  namespace

TEST_CASE("Job Object supervision terminates the real host when its owner "
          "process is killed",
          "[process][integration]") {
  const std::filesystem::path fixture{
      DOVAHLINK_ADAPTER_OWNER_LIFETIME_TEST_FIXTURE_EXE};
  REQUIRE(std::filesystem::exists(fixture));

  OwnerFixtureProcess owner(fixture);
  auto startup = owner.ReadStartup(std::chrono::seconds(10));
  REQUIRE(startup.has_value());

  auto rendezvousPath = ResolveDefaultRendezvousFilePath(std::get<0>(*startup));
  REQUIRE(rendezvousPath.has_value());
  ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);

  REQUIRE(owner.Terminate());
  REQUIRE(WaitForProcessExit(std::get<2>(*startup)));
  CHECK(WaitUntil([&] { return IsPortClosed(std::get<1>(*startup)); },
                  std::chrono::seconds(10)));
}

TEST_CASE("real hosts remain isolated by owner lifetime and shutdown signals",
          "[process][integration]") {
  const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
  REQUIRE(std::filesystem::exists(hostExecutable));

  const auto firstOwner = LifetimeIdWithMarker(std::byte{0xA1});
  const auto secondOwner = LifetimeIdWithMarker(std::byte{0xB2});
  auto firstPath = ResolveDefaultRendezvousFilePath(firstOwner);
  auto secondPath = ResolveDefaultRendezvousFilePath(secondOwner);
  REQUIRE(firstPath.has_value());
  REQUIRE(secondPath.has_value());
  ScopedRendezvousCleanup firstCleanup(*firstPath);
  ScopedRendezvousCleanup secondCleanup(*secondPath);

  std::error_code firstRemoveError;
  std::error_code secondRemoveError;
  std::filesystem::remove(*firstPath, firstRemoveError);
  std::filesystem::remove(*secondPath, secondRemoveError);

  //  Each real host's production Main entry point always composes a public
  //  WebSocket listener, defaulting to the fixed production port unless
  //  DOVAHLINK_TEST_PUBLIC_LISTENER_PORT overrides it -- so two real hosts
  //  launched without distinct overrides collide on that fixed port, and the
  //  second one's listener bind fails before it ever reports its endpoint.
  //  Each launch below gets its own override, scoped narrowly around the
  //  Launch() call: the child only ever reads the environment once, at
  //  CreateProcessW, so the guard can clear before the next launch begins.
  constexpr std::uint16_t kFirstPublicListenerPort = 58429;
  constexpr std::uint16_t kSecondPublicListenerPort = 58430;

  Win32AdapterHostProcessLauncher firstLauncher(hostExecutable, firstOwner,
                                                std::chrono::seconds(10));
  Win32AdapterHostProcessLauncher secondLauncher(hostExecutable, secondOwner,
                                                 std::chrono::seconds(10));
  std::optional<AdapterHostEndpoint> firstEndpoint;
  {
    ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort(
        kFirstPublicListenerPort);
    firstEndpoint = firstLauncher.Launch();
  }
  std::optional<AdapterHostEndpoint> secondEndpoint;
  {
    ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort(
        kSecondPublicListenerPort);
    secondEndpoint = secondLauncher.Launch();
  }
  REQUIRE(firstEndpoint.has_value());
  REQUIRE(secondEndpoint.has_value());
  CHECK(firstEndpoint->port != secondEndpoint->port);

  WindowsEventAdapterHostShutdownRequester firstShutdown(firstOwner);
  firstShutdown.RequestShutdown();
  REQUIRE(firstLauncher.AwaitExitOrTerminate(std::chrono::seconds(5)));
  CHECK(IsProcessStillRunning(secondLauncher.ProcessId()));

  WindowsEventAdapterHostShutdownRequester secondShutdown(secondOwner);
  secondShutdown.RequestShutdown();
  REQUIRE(secondLauncher.AwaitExitOrTerminate(std::chrono::seconds(5)));
}

TEST_CASE("the running supervisor rediscovers the real host on a new "
          "dynamic port after a host restart",
          "[process][integration]") {
  const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
  REQUIRE(std::filesystem::exists(hostExecutable));

  const auto ownerLifetimeId = LifetimeIdWithMarker(std::byte{0xC3});
  auto rendezvousPath = ResolveDefaultRendezvousFilePath(ownerLifetimeId);
  REQUIRE(rendezvousPath.has_value());
  ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);
  std::error_code removeError;
  std::filesystem::remove(*rendezvousPath, removeError);

  Win32AdapterHostProcessLauncher launcher(hostExecutable, ownerLifetimeId,
                                           std::chrono::seconds(10));
  auto firstEndpoint = launcher.Launch();
  REQUIRE(firstEndpoint.has_value());

  FileAdapterHostRendezvousReader reader(*rendezvousPath);
  WinsockAdapterIpcSocket connectionSocket(0);
  IpcFrameCodec codec;
  ImmediateTaskMarshaller taskMarshaller;
  AdapterNativeDispatcher dispatcher;
  NoopCaptureQueue captureQueue;
  NoopPairingNotificationSink pairingNotificationSink;
  std::unique_ptr<AdapterHostSupervisor> supervisor;
  AdapterIpcSession session(AdapterInstanceIdGenerator{}.Generate(),
                            ownerLifetimeId, taskMarshaller, dispatcher,
                            captureQueue, pairingNotificationSink);
  std::atomic<int> connectedCount = 0;
  std::mutex targetMutex;
  std::optional<dovahlink::adapter::ipc::AdapterIpcTarget> connectedTarget;
  AdapterIpcConnection connection(
      connectionSocket, codec,
      dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
          .onTargetConnected =
              [&](const dovahlink::adapter::ipc::AdapterIpcTarget &target) {
                {
                  std::lock_guard<std::mutex> lock(targetMutex);
                  connectedTarget = target;
                }
                ++connectedCount;
                session.HandleConnected(target);
              },
          .onMessageReceived =
              [&](const IpcMessage &message) {
                return session.HandleMessage(message);
              },
          .onDecodeFailure = [&] { session.HandleDecodeFailure(); },
          .onDisconnected = [&] { session.HandleDisconnected(); },
          .onAttemptFinished =
              [&](std::uint64_t targetGeneration,
                  dovahlink::adapter::ipc::AdapterIpcAttemptOutcome outcome) {
                supervisor->NotifyConnectionLost(targetGeneration, outcome);
              },
      });
  session.AttachConnection(connection);
  supervisor = std::make_unique<AdapterHostSupervisor>(
      reader, launcher, connection, std::chrono::milliseconds(50));
  supervisor->Start();

  REQUIRE(
      WaitUntil([&] { return connectionSocket.Port() == firstEndpoint->port; },
                std::chrono::seconds(5)));
  {
    std::lock_guard<std::mutex> lock(targetMutex);
    REQUIRE(connectedTarget.has_value());
    CHECK(connectedTarget->proofToken == firstEndpoint->proofToken);
    CHECK(connectedTarget->hostProofKey == firstEndpoint->hostProofKey);
  }

  REQUIRE(WaitUntil(
      [&] { return connectedCount.load() >= 1 && session.IsHostAvailable(); },
      std::chrono::seconds(5)));

  CHECK(IsProcessStillRunning(launcher.ProcessId()));

  //  Force the host down now, as an explicit step distinct from the liveness
  //  check above: this is what drives the supervisor to observe the loss and
  //  launch a replacement, proving the "after a host restart" rediscovery
  //  this test exists to cover.
  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));

  std::optional<AdapterHostEndpoint> secondEndpoint;
  REQUIRE(WaitUntil(
      [&] {
        secondEndpoint = reader.TryRead();
        return secondEndpoint.has_value() &&
               secondEndpoint->port != firstEndpoint->port;
      },
      std::chrono::seconds(10)));
  REQUIRE(secondEndpoint.has_value());
  REQUIRE(
      WaitUntil([&] { return connectionSocket.Port() == secondEndpoint->port; },
                std::chrono::seconds(5)));
  REQUIRE(WaitUntil(
      [&] { return connectedCount.load() >= 2 && session.IsHostAvailable(); },
      std::chrono::seconds(10)));
  {
    std::lock_guard<std::mutex> lock(targetMutex);
    REQUIRE(connectedTarget.has_value());
    CHECK(connectedTarget->proofToken == secondEndpoint->proofToken);
  }

  supervisor->RequestStop();
  connection.Stop();
  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  supervisor.reset();
}

///  Drives the real native `AdapterIpcSession`/`AdapterIpcConnection` pair
///  through a full Hello/HelloAck handshake against a real launched
///  `DovahLink.Host.exe`, connecting directly to the endpoint
///  `Win32AdapterHostProcessLauncher::Launch` already returns rather than
///  discovering it through a supervisor or rendezvous file -- this test is
///  concerned only with proving the private IPC wire agreement, not
///  discovery. Blocks the calling thread by construction, not by polling: the
///  fixture's task marshaller runs every game-thread dispatch inline, so no
///  Skyrim game-thread stand-in is needed to observe results.
class RealHostFixture {
public:
  ///  Launches a real host under a fresh, uniquely marked owner-lifetime id,
  ///  then connects and authenticates a real native session against it.
  ///  @param pairingSink Optional pairing-display sink override; when null,
  ///  falls back to a no-op sink, for tests not concerned with pairing
  ///  display.
  ///  @param hostExecutable The host executable to launch; defaults to the
  ///  framework-dependent DOVAHLINK_HOST_EXECUTABLE build every other test in
  ///  this file uses. A test proving behavior against the production-packaged
  ///  artifact shape passes DOVAHLINK_HOST_EXECUTABLE_SELFCONTAINED instead.
  explicit RealHostFixture(
      std::byte ownerLifetimeMarker,
      dovahlink::adapter::ipc::IAdapterPairingNotificationSink *pairingSink =
          nullptr,
      std::filesystem::path hostExecutable =
          std::filesystem::path(DOVAHLINK_HOST_EXECUTABLE))
      : hostExecutable_(std::move(hostExecutable)),
        ownerLifetimeId_(LifetimeIdWithMarker(ownerLifetimeMarker)),
        launcher_(hostExecutable_, ownerLifetimeId_, std::chrono::seconds(10)),
        session_(AdapterInstanceIdGenerator{}.Generate(), ownerLifetimeId_,
                 taskMarshaller_, dispatcher_, captureQueue_,
                 pairingSink != nullptr
                     ? *pairingSink
                     : static_cast<dovahlink::adapter::ipc::
                                       IAdapterPairingNotificationSink &>(
                           noopPairingNotificationSink_)),
        connection_(
            connectionSocket_, codec_,
            dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
                .onTargetConnected =
                    [this](const AdapterIpcTarget &target) {
                      session_.HandleConnected(target);
                    },
                .onMessageReceived =
                    [this](const IpcMessage &message) {
                      return session_.HandleMessage(message);
                    },
                .onDecodeFailure = [this] { session_.HandleDecodeFailure(); },
                .onDisconnected = [this] { session_.HandleDisconnected(); },
                .onAttemptFinished =
                    [](std::uint64_t,
                       dovahlink::adapter::ipc::AdapterIpcAttemptOutcome) {},
            }) {
    if (!std::filesystem::exists(hostExecutable_)) {
      throw std::runtime_error("DovahLink.Host.exe was not found at " +
                               hostExecutable_.string());
    }

    auto endpoint = launcher_.Launch();
    if (!endpoint.has_value()) {
      throw std::runtime_error("Unable to launch a real Host process.");
    }

    session_.AttachConnection(connection_);
    connection_.ConfigureTarget(
        AdapterIpcTarget{.port = endpoint->port,
                         .proofToken = endpoint->proofToken,
                         .hostProofKey = endpoint->hostProofKey,
                         .targetGeneration = 1});
    connection_.Start();

    if (!WaitUntil([this] { return session_.IsHostAvailable(); },
                   std::chrono::seconds(10))) {
      throw std::runtime_error(
          "The real host never completed Hello/HelloAck authentication.");
    }
  }

  ~RealHostFixture() {
    connection_.Stop();
    launcher_.AwaitExitOrTerminate(std::chrono::seconds(5));
  }

  RealHostFixture(const RealHostFixture &) = delete;
  RealHostFixture &operator=(const RealHostFixture &) = delete;

  ///  The authenticated session under test.
  AdapterIpcSession &Session() { return session_; }

private:
  std::filesystem::path hostExecutable_;
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
      ownerLifetimeId_;
  Win32AdapterHostProcessLauncher launcher_;
  WinsockAdapterIpcSocket connectionSocket_{0};
  IpcFrameCodec codec_;
  ImmediateTaskMarshaller taskMarshaller_;
  AdapterNativeDispatcher dispatcher_;
  NoopCaptureQueue captureQueue_;
  NoopPairingNotificationSink noopPairingNotificationSink_;
  AdapterIpcSession session_;
  AdapterIpcConnection connection_;
};

TEST_CASE("a real native adapter completes a trust-admin List request "
          "against a real launched Host, decoding its typed result",
          "[process][integration]") {
  //  Proves the actual C++ and C# TrustAdminOperation/TrustAdminListScope
  //  wire encodings agree, and that IpcTrustAdminRequestMessage's
  //  correlation id round-trips through a real Host's real handler and back
  //  into IpcTrustAdminResultMessage -- the cross-language ABI proof gap a
  //  same-process fake Host peer cannot close. The real Host's own trust
  //  store is a real DPAPI-backed store, not test-isolated per owner-lifetime
  //  id (that identity is only this test's process-launch/rendezvous
  //  scope), so this deliberately does not assert its content -- only that a
  //  well-formed, correctly correlated result decodes back.
  RealHostFixture fixture(std::byte{0xE5});

  auto resultPromise =
      std::make_shared<std::promise<TrustAdminRequestResult>>();
  std::future<TrustAdminRequestResult> resultFuture =
      resultPromise->get_future();
  fixture.Session().SendTrustAdminRequest(
      TrustAdminOperation::kList, TrustAdminListScope::kAll, std::nullopt,
      std::nullopt, [resultPromise](TrustAdminRequestResult result) {
        resultPromise->set_value(std::move(result));
      });

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
          std::future_status::ready);
  TrustAdminRequestResult result = resultFuture.get();
  REQUIRE(result.outcome == TrustAdminRequestOutcome::kCompleted);
  REQUIRE(result.resultText.has_value());
  CHECK_FALSE(result.resultText->empty());
  CHECK(std::regex_search(
      *result.resultText,
      std::regex(R"(^(No known devices\.|\d+ known devices?:))")));
}

TEST_CASE("a real native adapter observes a real Host's pairing-display "
          "notification and acknowledges it, driven by a real public "
          "pairing_request",
          "[process][integration]") {
  //  Proves the cross-language direction the trust-admin List E2E above does
  //  not cover: a real public client's pairing_request drives the real
  //  Host's real PairingCoordinator to decide to display a code, which the
  //  real Host encodes into a real IpcPairingDisplayMessage and sends over
  //  the real private IPC socket; this real native session decodes it,
  //  dispatches it onto the game thread (inline, via this fixture's
  //  ImmediateTaskMarshaller), presents it through a recording sink, and
  //  encodes a real IpcPairingDisplayAckMessage the real Host receives and
  //  resolves into a committed pairing_status. The public listener only
  //  opens here because this one test sets
  //  DOVAHLINK_TEST_PUBLIC_LISTENER_PORT before launching the real Host --
  //  see Program::ParseTestPublicListenerPort's own documentation for why
  //  the production launch path never does.
  constexpr std::uint16_t kPublicListenerPort = 58427;
  ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort(
      kPublicListenerPort);
  RecordingPairingNotificationSink pairingSink;
  RealHostFixture fixture(std::byte{0xE6}, &pairingSink);

  MinimalPublicWebSocketClient client(kPublicListenerPort);
  client.SendText(
      R"({"messageType":"hello","messageId":"m1","sessionId":null,)"
      R"("correlationId":null,"payload":{"endpoint":"client",)"
      R"("clientId":"e6e6e6e6-e6e6-e6e6-e6e6-e6e6e6e6e6e6","auth":{"method":)"
      R"("unpaired"}},"bridgeInstanceId":null,"playContextId":null,)"
      R"("clientId":null})");
  std::string helloAck = client.ReceiveText();
  REQUIRE(helloAck.find(R"("messageType":"hello_ack")") != std::string::npos);
  //  Every subsequent message must echo the real Host-assigned session id
  //  exactly, or it is rejected as stale.
  std::string sessionId = ExtractJsonStringField(helloAck, "sessionId");
  REQUIRE_FALSE(sessionId.empty());
  //  Every admission sends hello_ack followed by an unsolicited, empty
  //  capabilities advertisement -- consumed here so it is never mistaken
  //  for the pairing_status reply below.
  std::string capabilities = client.ReceiveText();
  REQUIRE(capabilities.find(R"("messageType":"capabilities")") !=
          std::string::npos);

  //  A post-admission client message must carry both the socket-bound
  //  sessionId and the envelope-level clientId it declared in hello -- null
  //  was only ever valid pre-admission.
  client.SendText(
      R"({"messageType":"pairing_request","messageId":"m2","sessionId":")" +
      sessionId +
      R"(","correlationId":null,"payload":{},"bridgeInstanceId":null,)"
      R"("playContextId":null,"clientId":"e6e6e6e6-e6e6-e6e6-e6e6-e6e6e6e6e6e6"})");

  REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                    std::chrono::seconds(10)));
  auto displayed = pairingSink.Displayed();
  REQUIRE(displayed.size() == 1);
  const auto &[code, mode] = displayed.front();
  //  The code survived real C#-encode/real C++-decode over the wire:
  //  non-empty and well-formed, the shape PairingCoordinator generates.
  CHECK_FALSE(code.empty());
  CHECK(std::ranges::all_of(code, [](char c) { return c >= '0' && c <= '9'; }));
  //  PairingDisplayMode::kInitial agrees across C++ and C#: a mismatched
  //  wire encoding would decode to a different mode value here.
  CHECK(mode == dovahlink::adapter::ipc::PairingDisplayMode::kInitial);

  //  The real Host received and resolved the real IpcPairingDisplayAckMessage
  //  this session sent back: TryNotifyCodeAvailableAsync only returns true,
  //  producing this "available" status, once its own
  //  AwaitPairingDisplayAckAsync observed an ack correlated to the exact
  //  request it sent -- a wrong or missing correlation id would time out into
  //  "unavailable" instead.
  std::string pairingStatus = client.ReceiveText();
  CHECK(pairingStatus.find(R"("messageType":"pairing_status")") !=
        std::string::npos);
  CHECK(pairingStatus.find(R"("state":"available")") != std::string::npos);
  //  The displayed code must never appear in the public wire response.
  CHECK(pairingStatus.find(code) == std::string::npos);
}

TEST_CASE("a real native adapter acknowledges a rejected pairing-display "
          "request, and the real Host reports it unavailable rather than "
          "committing a challenge",
          "[process][integration]") {
  //  The rejected-ack counterpart to the accepted-display E2E above: the
  //  recording sink refuses the display, the real native session still
  //  encodes and sends a real IpcPairingDisplayAckMessage with accepted =
  //  false, and the real Host's own rollback path reports pairing_status
  //  unavailable rather than committing a challenge no adapter actually
  //  presented.
  constexpr std::uint16_t kPublicListenerPort = 58428;
  ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort(
      kPublicListenerPort);
  RecordingPairingNotificationSink pairingSink;
  pairingSink.SetAcceptDisplay(false);
  RealHostFixture fixture(std::byte{0xE7}, &pairingSink);

  MinimalPublicWebSocketClient client(kPublicListenerPort);
  client.SendText(
      R"({"messageType":"hello","messageId":"m1","sessionId":null,)"
      R"("correlationId":null,"payload":{"endpoint":"client",)"
      R"("clientId":"e7e7e7e7-e7e7-e7e7-e7e7-e7e7e7e7e7e7","auth":{"method":)"
      R"("unpaired"}},"bridgeInstanceId":null,"playContextId":null,)"
      R"("clientId":null})");
  std::string helloAck = client.ReceiveText();
  REQUIRE(helloAck.find(R"("messageType":"hello_ack")") != std::string::npos);
  //  Every subsequent message must echo the real Host-assigned session id
  //  exactly, or it is rejected as stale.
  std::string sessionId = ExtractJsonStringField(helloAck, "sessionId");
  REQUIRE_FALSE(sessionId.empty());
  //  Every admission sends hello_ack followed by an unsolicited, empty
  //  capabilities advertisement -- consumed here so it is never mistaken
  //  for the pairing_status reply below.
  std::string capabilities = client.ReceiveText();
  REQUIRE(capabilities.find(R"("messageType":"capabilities")") !=
          std::string::npos);

  //  A post-admission client message must carry both the socket-bound
  //  sessionId and the envelope-level clientId it declared in hello -- null
  //  was only ever valid pre-admission.
  client.SendText(
      R"({"messageType":"pairing_request","messageId":"m2","sessionId":")" +
      sessionId +
      R"(","correlationId":null,"payload":{},"bridgeInstanceId":null,)"
      R"("playContextId":null,"clientId":"e7e7e7e7-e7e7-e7e7-e7e7-e7e7e7e7e7e7"})");

  REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                    std::chrono::seconds(10)));
  CHECK(pairingSink.Displayed().size() == 1);

  std::string pairingStatus = client.ReceiveText();
  CHECK(pairingStatus.find(R"("messageType":"pairing_status")") !=
        std::string::npos);
  CHECK(pairingStatus.find(R"("state":"unavailable")") != std::string::npos);
}

TEST_CASE("a rendezvous port occupied by another process falls back to a "
          "fresh host after the connection attempt fails",
          "[process][integration]") {
  const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
  REQUIRE(std::filesystem::exists(hostExecutable));

  const auto ownerLifetimeId = LifetimeIdWithMarker(std::byte{0xD4});
  auto rendezvousPath = ResolveDefaultRendezvousFilePath(ownerLifetimeId);
  REQUIRE(rendezvousPath.has_value());
  ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);

  ScopedLoopbackListener staleListener;
  {
    std::ofstream rendezvousFile(*rendezvousPath);
    REQUIRE(rendezvousFile.is_open());
    rendezvousFile << "PORT " << staleListener.Port()
                   << "\nPROOF aa\nHOSTPROOF bb\n";
  }

  FileAdapterHostRendezvousReader reader(*rendezvousPath);
  auto staleEndpoint = reader.TryRead();
  REQUIRE(staleEndpoint.has_value());
  CHECK(staleEndpoint->port == staleListener.Port());

  Win32AdapterHostProcessLauncher launcher(hostExecutable, ownerLifetimeId,
                                           std::chrono::seconds(10));
  RecordingAdapterIpcConnection connection;
  AdapterHostSupervisor supervisor(reader, launcher, connection,
                                   std::chrono::milliseconds(50));
  connection.SetOnStartCallback([&] {
    if (connection.StartCount() == 1) {
      supervisor.NotifyConnectionLost(
          1, dovahlink::adapter::ipc::AdapterIpcAttemptOutcome::kConnectFailed);
    }
  });

  supervisor.Start();

  REQUIRE(WaitUntil(
      [&] {
        return connection.StartCount() == 2 &&
               connection.ConfiguredPort() != staleListener.Port() &&
               connection.ConfiguredPort() != 0;
      },
      std::chrono::seconds(10)));

  auto freshEndpoint = reader.TryRead();
  REQUIRE(freshEndpoint.has_value());
  CHECK(connection.ConfiguredPort() == freshEndpoint->port);
  CHECK(connection.ConfiguredProofToken() == freshEndpoint->proofToken);
  CHECK(connection.ConfiguredHostProofKey() == freshEndpoint->hostProofKey);

  supervisor.RequestStop();
  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}

TEST_CASE("a real native adapter completes Hello/HelloAck against a real "
          "self-contained published Host executable",
          "[process][integration][packaging]") {
  //  Proves the adapter's real launch, private-IPC connect, and Hello/HelloAck
  //  authentication path against the actual production-packaged artifact
  //  shape -- self-contained, single-file, published via `dotnet publish`
  //  (the same production publishing strategy tooling/package_adapter_host.py
  //  uses) -- rather than only the framework-dependent `dotnet build` output
  //  every other real-process test in this file launches against. This is
  //  the same full proof RealHostFixture already gives every other test in
  //  this file, just pointed at the packaged executable shape; a real
  //  Skyrim/SKSE session remains a separate manual verification step.
  RealHostFixture fixture(
      std::byte{0xF8}, /*pairingSink=*/nullptr,
      std::filesystem::path(DOVAHLINK_HOST_EXECUTABLE_SELFCONTAINED));

  CHECK(fixture.Session().IsHostAvailable());
}
