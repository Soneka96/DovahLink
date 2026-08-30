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
#include "process/adapter_host_handshake_verifier.hpp"
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
#include <iostream>
#include <memory>
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
using dovahlink::adapter::process::AdapterHostHandshakeVerifier;
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

} //  namespace

TEST_CASE("the native launcher and verifier complete the real C# host "
          "dynamic-port handshake",
          "[process][integration]") {
  const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
  REQUIRE(std::filesystem::exists(hostExecutable));

  const auto ownerLifetimeId = DeriveOwnerLifetimeId();
  const auto rendezvousPath = ResolveDefaultRendezvousFilePath(ownerLifetimeId);
  REQUIRE(rendezvousPath.has_value());
  ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);

  std::error_code removeError;
  std::filesystem::remove(*rendezvousPath, removeError);

  Win32AdapterHostProcessLauncher launcher(hostExecutable, ownerLifetimeId,
                                           std::chrono::seconds(10));
  std::optional<AdapterHostEndpoint> launchedEndpoint = launcher.Launch();

  REQUIRE(launchedEndpoint.has_value());
  CHECK(launchedEndpoint->port != 0);
  REQUIRE(launchedEndpoint->proofToken.size() ==
          dovahlink::adapter::ipc::kMaxIpcPeerProofTokenBytes);

  FileAdapterHostRendezvousReader reader(*rendezvousPath);
  std::optional<AdapterHostEndpoint> fileEndpoint = reader.TryRead();
  REQUIRE(fileEndpoint.has_value());
  CHECK(*fileEndpoint == *launchedEndpoint);

  AdapterInstanceIdGenerator idGenerator;
  const auto adapterInstanceId = idGenerator.Generate();
  WinsockAdapterIpcSocket verifierSocket(launchedEndpoint->port);
  IpcFrameCodec codec;
  AdapterHostHandshakeVerifier verifier(adapterInstanceId, ownerLifetimeId,
                                        verifierSocket, codec,
                                        std::chrono::seconds(2));

  CHECK(verifier.Verify(*launchedEndpoint));

  AdapterHostEndpoint forgedEndpoint = *launchedEndpoint;
  forgedEndpoint.proofToken.front() ^= std::byte{0xFF};
  CHECK_FALSE(verifier.Verify(forgedEndpoint));

  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}

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

TEST_CASE("real hosts remain isolated by owner lifetime for authentication "
          "and shutdown signals",
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

  AdapterInstanceIdGenerator idGenerator;
  IpcFrameCodec codec;
  WinsockAdapterIpcSocket secondVerifierSocket(firstEndpoint->port);
  AdapterHostHandshakeVerifier secondVerifier(idGenerator.Generate(),
                                              secondOwner, secondVerifierSocket,
                                              codec, std::chrono::seconds(2));
  CHECK_FALSE(secondVerifier.Verify(*firstEndpoint));

  WinsockAdapterIpcSocket firstToSecondVerifierSocket(secondEndpoint->port);
  AdapterHostHandshakeVerifier firstToSecondVerifier(
      idGenerator.Generate(), firstOwner, firstToSecondVerifierSocket, codec,
      std::chrono::seconds(2));
  CHECK_FALSE(firstToSecondVerifier.Verify(*secondEndpoint));

  WindowsEventAdapterHostShutdownRequester firstShutdown(firstOwner);
  firstShutdown.RequestShutdown();
  REQUIRE(firstLauncher.AwaitExitOrTerminate(std::chrono::seconds(5)));

  WinsockAdapterIpcSocket secondAfterFirstShutdownSocket(secondEndpoint->port);
  AdapterHostHandshakeVerifier secondAfterFirstShutdown(
      idGenerator.Generate(), secondOwner, secondAfterFirstShutdownSocket,
      codec, std::chrono::seconds(2));
  CHECK(secondAfterFirstShutdown.Verify(*secondEndpoint));

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
  WinsockAdapterIpcSocket verifierSocket(0);
  WinsockAdapterIpcSocket connectionSocket(0);
  IpcFrameCodec codec;
  AdapterHostHandshakeVerifier verifier(AdapterInstanceIdGenerator{}.Generate(),
                                        ownerLifetimeId, verifierSocket, codec,
                                        std::chrono::seconds(2));
  SettableAdapterIpcPeerProofProvider proofProvider;
  ImmediateTaskMarshaller taskMarshaller;
  AdapterNativeDispatcher dispatcher;
  NoopCaptureQueue captureQueue;
  std::unique_ptr<AdapterHostSupervisor> supervisor;
  AdapterIpcSession session(AdapterInstanceIdGenerator{}.Generate(),
                            ownerLifetimeId, proofProvider, taskMarshaller,
                            dispatcher, captureQueue);
  std::atomic<int> connectedCount = 0;
  AdapterIpcConnection connection(
      connectionSocket, codec,
      dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
          .onConnected =
              [&] {
                ++connectedCount;
                session.HandleConnected();
              },
          .onMessageReceived =
              [&](const IpcMessage &message) {
                return session.HandleMessage(message);
              },
          .onDecodeFailure = [&] { session.HandleDecodeFailure(); },
          .onDisconnected =
              [&] {
                session.HandleDisconnected();
                supervisor->NotifyConnectionLost();
              },
      });
  session.AttachConnection(connection);
  supervisor = std::make_unique<AdapterHostSupervisor>(
      reader, verifier, verifierSocket, launcher, connectionSocket,
      proofProvider, connection, std::chrono::milliseconds(50));
  supervisor->Start();

  REQUIRE(
      WaitUntil([&] { return connectionSocket.Port() == firstEndpoint->port; },
                std::chrono::seconds(5)));
  CHECK(proofProvider.Token() == firstEndpoint->proofToken);

  REQUIRE(WaitUntil(
      [&] { return connectedCount.load() >= 1 && session.IsHostAvailable(); },
      std::chrono::seconds(5)));

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
  CHECK(proofProvider.Token() == secondEndpoint->proofToken);

  supervisor->RequestStop();
  connection.Stop();
  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  supervisor.reset();
}
