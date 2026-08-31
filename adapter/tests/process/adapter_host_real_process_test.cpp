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
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <system_error>
#include <thread>
#include <tuple>
#include <utility>

using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::dispatch::AdapterNativeDispatcher;
using dovahlink::adapter::identity::AdapterInstanceIdGenerator;
using dovahlink::adapter::ipc::AdapterIpcConnection;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::SettableAdapterIpcPeerProofProvider;
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

  Win32AdapterHostProcessLauncher firstLauncher(hostExecutable, firstOwner,
                                                std::chrono::seconds(10));
  Win32AdapterHostProcessLauncher secondLauncher(hostExecutable, secondOwner,
                                                 std::chrono::seconds(10));
  auto firstEndpoint = firstLauncher.Launch();
  auto secondEndpoint = secondLauncher.Launch();
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
  std::unique_ptr<AdapterHostSupervisor> supervisor;
  AdapterIpcSession session(AdapterInstanceIdGenerator{}.Generate(),
                            ownerLifetimeId, taskMarshaller, dispatcher,
                            captureQueue);
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
