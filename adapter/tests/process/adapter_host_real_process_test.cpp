#include "capture/adapter_capture_handoff_queue.hpp"
#include "capture/live_state_sample_codec.hpp"
#include "constants.hpp"
#include "dispatch/adapter_native_capture_router.hpp"
#include "enums.hpp"
#include "identity/adapter_instance_id_generator.hpp"
#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_session.hpp"
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
#include <cctype>
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
#include <random>
#include <regex>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <tuple>
#include <utility>
#include <vector>

using dovahlink::adapter::capture::AdapterCaptureHandoffQueue;
using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::CaptureAvailability;
using dovahlink::adapter::capture::CapturedPayload;
using dovahlink::adapter::capture::CaptureSourceKind;
using dovahlink::adapter::capture::CharacterEventKey;
using dovahlink::adapter::capture::CharacterSampleToken;
using dovahlink::adapter::capture::EncodeFloatLittleEndian;
using dovahlink::adapter::capture::EncodeUInt16LittleEndian;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::capture::MakeCapturedPayload;
using dovahlink::adapter::dispatch::AdapterNativeCaptureRouter;
using dovahlink::adapter::dispatch::IAdapterNativeCaptureRouter;
using dovahlink::adapter::dispatch::SampleCaptureResult;
using dovahlink::adapter::dispatch::SampleCaptureStatus;
using dovahlink::adapter::identity::AdapterInstanceIdGenerator;
using dovahlink::adapter::identity::AdapterPlayContextState;
using dovahlink::adapter::identity::IAdapterPlayContextState;
using dovahlink::adapter::ipc::AdapterIpcConnection;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::AdapterIpcTarget;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcListenEventMessage;
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
using dovahlink::adapter::process::kAdapterHostExecutableRelativePath;
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

    ScopedRendezvousCleanup(const ScopedRendezvousCleanup&) = delete;
    ScopedRendezvousCleanup& operator=(const ScopedRendezvousCleanup&) = delete;

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

    OwnerFixtureProcess(const OwnerFixtureProcess&) = delete;
    OwnerFixtureProcess& operator=(const OwnerFixtureProcess&) = delete;

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

///  Accepts every registration and reports every sample unavailable, since
///  this cross-process test is concerned with IPC connection recovery, not
///  resync outcome. The generic, production `AdapterNativeCaptureRouter`
///  approves no token at all, which makes every resynchronize request this
///  fixture's automatic post-handshake send and any later trigger produce
///  come back declined -- and the Host closes the connection on a genuinely
///  declined resync result. Every test built on this fixture that assumes
///  the connection stays open would otherwise be racing that close.
class AcceptingCaptureRouter final
    : public dovahlink::adapter::dispatch::IAdapterNativeCaptureRouter {
  public:
    dovahlink::adapter::dispatch::SampleCaptureResult
    CaptureSample(std::uint32_t) override {
        return dovahlink::adapter::dispatch::SampleCaptureResult{
            .status =
                dovahlink::adapter::dispatch::SampleCaptureStatus::kUnavailable};
    }
    bool RegisterEvent(std::uint32_t) override { return true; }
};

///  A deterministic, TEST-ONLY stand-in for a real Skyrim capture, used only
///  by the live-state E2E tests below. Mirrors
///  `CommonLibAdapterNativeCaptureRouter::CaptureSample`'s exact switch
///  shape and wire encoding (via the same production
///  `EncodeFloatLittleEndian`/`EncodeUInt16LittleEndian`/`MakeCapturedPayload`
///  helpers) so the real Host's real resynchronization plan -- which
///  currently requires the full Character baseline, not XP alone -- actually
///  completes. `EmitLevelChanged` additionally mirrors
///  `CommonLibAdapterNativeCaptureRouter::LevelChangedEventSink::ProcessEvent`,
///  the real `RE::LevelIncrease::Event` sink, so the Level Event E2E test can
///  simulate that native callback without touching Skyrim/CommonLib/SKSE,
///  keeping CI deterministic.
class DeterministicBaselineCaptureRouter final
    : public IAdapterNativeCaptureRouter {
  public:
    SampleCaptureResult CaptureSample(std::uint32_t sampleToken) override {
        switch (static_cast<CharacterSampleToken>(sampleToken)) {
        case CharacterSampleToken::kCharacterVitals: {
            std::array<std::byte, 4> health = EncodeFloatLittleEndian(100.0f);
            std::array<std::byte, 4> magicka = EncodeFloatLittleEndian(80.0f);
            std::array<std::byte, 4> stamina = EncodeFloatLittleEndian(90.0f);
            CapturedPayload payload;
            std::ranges::copy(health, payload.bytes.begin());
            std::ranges::copy(magicka, payload.bytes.begin() + 4);
            std::ranges::copy(stamina, payload.bytes.begin() + 8);
            payload.size = 12;
            return SampleCaptureResult{.status = SampleCaptureStatus::kAvailable,
                                       .payload = payload};
        }
        case CharacterSampleToken::kCharacterXp: {
            std::array<std::byte, 4> encoded = EncodeFloatLittleEndian(42.5f);
            return SampleCaptureResult{.status = SampleCaptureStatus::kAvailable,
                                       .payload = MakeCapturedPayload(encoded)};
        }
        case CharacterSampleToken::kCharacterLevelBaseline: {
            std::array<std::byte, 2> encoded = EncodeUInt16LittleEndian(10);
            return SampleCaptureResult{.status = SampleCaptureStatus::kAvailable,
                                       .payload = MakeCapturedPayload(encoded)};
        }
        default:
            return SampleCaptureResult{.status = SampleCaptureStatus::kUnsupported};
        }
    }

    bool RegisterEvent(std::uint32_t eventKey) override {
        if (static_cast<CharacterEventKey>(eventKey) !=
            CharacterEventKey::kCharacterLevelChanged) {
            return false;
        }
        levelChangedRegistered_.store(true, std::memory_order_relaxed);
        return true;
    }

    ///  Wires the real collaborators `EmitLevelChanged` enqueues into,
    ///  mirroring `LevelChangedEventSink`'s own constructor-injected
    ///  collaborators. Called once, from the owning fixture's constructor
    ///  body, after its capture queue and play-context state are fully
    ///  constructed -- the same after-construction attachment pattern
    ///  `AdapterIpcSession::AttachConnection` already uses in this file, so
    ///  no member-declaration reordering is needed.
    ///  @param captureQueue The real queue `EmitLevelChanged` enqueues into.
    ///  @param playContextState The real play-context state `EmitLevelChanged`
    ///  stamps its work item from.
    void AttachLevelChangedEmitter(IAdapterCaptureHandoffQueue& captureQueue,
                                   IAdapterPlayContextState& playContextState) {
        captureQueue_ = &captureQueue;
        playContextState_ = &playContextState;
    }

    ///  Whether the real Host-driven resynchronization plan has actually
    ///  registered `CharacterLevelChanged` through `RegisterEvent`, proving
    ///  `EmitLevelChanged` below enters through a real registration rather
    ///  than firing unconditionally.
    ///  @return `true` once `RegisterEvent` has accepted `CharacterLevelChanged`.
    bool IsLevelChangedRegistered() const {
        return levelChangedRegistered_.load(std::memory_order_relaxed);
    }

    ///  Test-only stand-in for the real `RE::LevelIncrease::Event` sink's
    ///  `ProcessEvent`, enqueuing the exact same `AdapterCaptureWorkItem`
    ///  shape into the same real `AdapterCaptureHandoffQueue` the owning
    ///  fixture drains onto `SendCaptureResult`.
    ///  @param newLevel The simulated new character level.
    void EmitLevelChanged(std::uint16_t newLevel) {
        if (!levelChangedRegistered_.load(std::memory_order_relaxed) ||
            captureQueue_ == nullptr || playContextState_ == nullptr) {
            throw std::logic_error(
                "EmitLevelChanged called before CharacterLevelChanged was "
                "registered and attached.");
        }
        std::array<std::byte, 2> encoded = EncodeUInt16LittleEndian(newLevel);
        captureQueue_->TryEnqueue(AdapterCaptureWorkItem{
            .intentKey =
                static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged),
            .capturedValue = MakeCapturedPayload(encoded),
            .correlationId = 0,
            .source = CaptureSourceKind::kEvent,
            .availability = CaptureAvailability::kAvailable,
            .playContextId = playContextState_->CurrentPlayContext().value_or(
                std::array<std::byte, 16>{}),
        });
    }

  private:
    ///  Whether `RegisterEvent` has ever accepted `CharacterLevelChanged`.
    ///  Atomic: written from the private IPC session's own execution thread
    ///  inside `RegisterEvent`, read from the test thread by
    ///  `IsLevelChangedRegistered` and `EmitLevelChanged`.
    std::atomic<bool> levelChangedRegistered_{false};
    ///  The real capture queue `EmitLevelChanged` enqueues into. Not owned;
    ///  set once by `AttachLevelChangedEmitter`.
    IAdapterCaptureHandoffQueue* captureQueue_ = nullptr;
    ///  The real play-context state `EmitLevelChanged` stamps its work item
    ///  from. Not owned; set once by `AttachLevelChangedEmitter`.
    IAdapterPlayContextState* playContextState_ = nullptr;
};

///  Presents nothing, since this cross-process test is concerned with IPC
///  connection recovery, not pairing display.
class NoopPairingNotificationSink final
    : public dovahlink::adapter::ipc::IAdapterPairingNotificationSink {
  public:
    bool Display(const std::string&,
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
    bool Display(const std::string& code,
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
        if (connect(socket_, reinterpret_cast<const sockaddr*>(&address),
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
                       reinterpret_cast<const char*>(&kReceiveTimeoutMilliseconds),
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

    MinimalPublicWebSocketClient(const MinimalPublicWebSocketClient&) = delete;
    MinimalPublicWebSocketClient&
    operator=(const MinimalPublicWebSocketClient&) = delete;

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
        SendRaw(reinterpret_cast<const char*>(frame.data()), frame.size());
    }

    ///  Reads one complete, unfragmented, unmasked WebSocket text frame from
    ///  the server and returns its payload.
    std::string ReceiveText() {
        std::array<std::uint8_t, 2> header{};
        ReadExact(reinterpret_cast<char*>(header.data()), header.size());
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
            ReadExact(reinterpret_cast<char*>(extended.data()), extended.size());
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
    void SendRaw(const char* data, std::size_t size) {
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
    void ReadExact(char* data, std::size_t size) {
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
        const ScopedTestPublicListenerPortEnvironmentVariable&) = delete;
    ScopedTestPublicListenerPortEnvironmentVariable&
    operator=(const ScopedTestPublicListenerPortEnvironmentVariable&) = delete;
};

///  A fresh, unique temporary trust-store file path per real Host launch, so
///  parallel and repeated test runs never collide -- mirrors
///  adapter_host_rendezvous_reader_test.cpp's own UniqueTempFilePath.
std::filesystem::path UniqueTempTrustStorePath() {
    std::mt19937_64 engine{std::random_device{}()};
    return std::filesystem::temp_directory_path() /
           ("dovahlink-trust-store-test-" + std::to_string(engine()) + ".dat");
}

///  RAII guard that sets `DOVAHLINK_TEST_TRUST_STORE_PATH` to a unique
///  per-test file for the scope of a single real Host launch, then clears it
///  and deletes the file -- so every real Host `RealHostFixture` launches
///  persists trust to a private file instead of the real per-Windows-user
///  DPAPI-backed store, and no other test sharing this process's environment
///  block ever observes it set.
class ScopedTestTrustStorePathEnvironmentVariable {
  public:
    ScopedTestTrustStorePathEnvironmentVariable()
        : path_(UniqueTempTrustStorePath()) {
        if (_putenv_s("DOVAHLINK_TEST_TRUST_STORE_PATH", path_.string().c_str()) !=
            0) {
            throw std::runtime_error(
                "Unable to set the trust-store-path environment variable.");
        }
    }

    ~ScopedTestTrustStorePathEnvironmentVariable() {
        _putenv_s("DOVAHLINK_TEST_TRUST_STORE_PATH", "");
        std::error_code error;
        std::filesystem::remove(path_, error);
    }

    ScopedTestTrustStorePathEnvironmentVariable(
        const ScopedTestTrustStorePathEnvironmentVariable&) = delete;
    ScopedTestTrustStorePathEnvironmentVariable&
    operator=(const ScopedTestTrustStorePathEnvironmentVariable&) = delete;

    ///  The isolated trust-store file this scope's real Host was launched
    ///  with, for a test to assert against.
    const std::filesystem::path& Path() const { return path_; }

  private:
    ///  The unique per-test trust-store file this guard set and will clean up.
    std::filesystem::path path_;
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
        if (bind(listener_, reinterpret_cast<const sockaddr*>(&address),
                 sizeof(address)) == SOCKET_ERROR ||
            listen(listener_, 1) == SOCKET_ERROR) {
            throw std::runtime_error("Unable to bind stale listener socket.");
        }

        int addressLength = sizeof(address);
        if (getsockname(listener_, reinterpret_cast<sockaddr*>(&address),
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

    ScopedLoopbackListener(const ScopedLoopbackListener&) = delete;
    ScopedLoopbackListener& operator=(const ScopedLoopbackListener&) = delete;

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
    bool TrySend(const IpcMessage&) override { return true; }

    ///  The fallback test never resets this connection.
    void RequestReconnect() override {}

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
std::string ExtractJsonStringField(const std::string& json,
                                   const std::string& key) {
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

///  Extracts a top-level JSON numeric field's value by key from `json`,
///  using the same plain substring search as `ExtractJsonStringField` --
///  sufficient for this test's own real, well-formed server responses.
///  Returns `std::nullopt` if `key` is absent or its value is not a bare
///  numeric literal.
std::optional<double> ExtractJsonNumberField(const std::string& json,
                                             const std::string& key) {
    const std::string marker = "\"" + key + "\":";
    std::size_t start = json.find(marker);
    if (start == std::string::npos) {
        return std::nullopt;
    }
    start += marker.size();
    std::size_t end = start;
    while (end < json.size() &&
           (std::isdigit(static_cast<unsigned char>(json[end])) != 0 ||
            json[end] == '.' || json[end] == '-' || json[end] == '+')) {
        ++end;
    }
    if (end == start) {
        return std::nullopt;
    }
    try {
        return std::stod(json.substr(start, end - start));
    } catch (const std::exception&) {
        return std::nullopt;
    }
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

///  Receives text frames from `client` until one whose top-level
///  `"messageType"` field equals `expectedMessageType`, skipping any
///  legitimate protocol message that may legitimately precede it (for
///  example a subscription_ack before its own baseline state_snapshot),
///  bounded by `timeout` rather than looping unboundedly. Each individual
///  `ReceiveText()` call already carries its own bounded socket receive
///  timeout; this additionally bounds the total number of skippable frames
///  this helper will wait through.
std::string ReceiveUntil(MinimalPublicWebSocketClient& client,
                         const std::string& expectedMessageType,
                         std::chrono::milliseconds timeout) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < deadline) {
        std::string frame = client.ReceiveText();
        if (ExtractJsonStringField(frame, "messageType") == expectedMessageType) {
            return frame;
        }
    }
    throw std::runtime_error("Timed out waiting for a \"" + expectedMessageType +
                             "\" message from the real Host's public listener.");
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
    AdapterNativeCaptureRouter captureRouter;
    NoopCaptureQueue captureQueue;
    NoopPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    std::unique_ptr<AdapterHostSupervisor> supervisor;
    AdapterIpcSession session(AdapterInstanceIdGenerator{}.Generate(),
                              ownerLifetimeId, taskMarshaller, captureRouter,
                              captureQueue, pairingNotificationSink,
                              playContextState);
    std::atomic<int> connectedCount = 0;
    std::mutex targetMutex;
    std::optional<dovahlink::adapter::ipc::AdapterIpcTarget> connectedTarget;
    AdapterIpcConnection connection(
        connectionSocket, codec,
        dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
            .onTargetConnected =
                [&](const dovahlink::adapter::ipc::AdapterIpcTarget& target) {
                    {
                        std::lock_guard<std::mutex> lock(targetMutex);
                        connectedTarget = target;
                    }
                    ++connectedCount;
                    session.HandleConnected(target);
                },
            .onMessageReceived =
                [&](const IpcMessage& message) {
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

TEST_CASE("AdapterIpcConnection::RequestReconnect resets the connection and "
          "the supervisor re-establishes a fresh authenticated generation "
          "against the same real host, replaying the same active play "
          "context into exactly one fresh resync",
          "[process][integration]") {
    //  AdapterRuntime's own tests (adapter_runtime_test.cpp) already prove
    //  that a rejected reliable Event's onRejected callback calls
    //  RequestReconnect() (and that a rejected Snapshot sample does not);
    //  this test proves the other half -- what RequestReconnect() itself
    //  actually does end to end against a real host -- so it calls it
    //  directly rather than re-deriving a queue rejection. Unlike a host
    //  restart (the test above), this never kills the real host process: it
    //  only resets this one connection's current attempt. Stopping the
    //  connection permanently instead (as Stop() deliberately does) would
    //  leave Start() unable to ever reconnect; this proves RequestReconnect()
    //  instead lets the same still-running host be rediscovered and
    //  re-authenticated, with a fresh resync.
    //
    //  A bare Hello/HelloAck never earns a resync by itself -- only the
    //  authenticated replay of an ACTIVE AdapterPlayContextState does (see
    //  AdapterIpcSession::ReplayCurrentPlayContextState). So this test
    //  establishes an active play context A through the normal
    //  AdapterPlayContextState API before G1 ever connects, and keeps that
    //  same context A across the reconnect: G2 replays the SAME context A
    //  again and must still earn its own fresh resync, proving a reconnect
    //  re-establishes authoritative state even though Skyrim never left the
    //  same loaded play context.
    const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
    REQUIRE(std::filesystem::exists(hostExecutable));

    const auto ownerLifetimeId = LifetimeIdWithMarker(std::byte{0xC4});
    auto rendezvousPath = ResolveDefaultRendezvousFilePath(ownerLifetimeId);
    REQUIRE(rendezvousPath.has_value());
    ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);
    std::error_code removeError;
    std::filesystem::remove(*rendezvousPath, removeError);

    Win32AdapterHostProcessLauncher launcher(hostExecutable, ownerLifetimeId,
                                             std::chrono::seconds(10));
    auto endpoint = launcher.Launch();
    REQUIRE(endpoint.has_value());

    FileAdapterHostRendezvousReader reader(*rendezvousPath);
    WinsockAdapterIpcSocket connectionSocket(0);
    IpcFrameCodec codec;
    ImmediateTaskMarshaller taskMarshaller;
    //  AcceptingCaptureRouter, not the generic production
    //  AdapterNativeCaptureRouter: this test now actually exercises the
    //  resync path (an active play context earns a real
    //  IpcResynchronizeRequestMessage), and per AcceptingCaptureRouter's own
    //  documentation above, the generic router approves no token at all,
    //  which comes back declined and makes the real Host close the
    //  connection on a genuinely declined resync result -- exactly the kind
    //  of connection churn this test exists to rule out, not exercise.
    AcceptingCaptureRouter captureRouter;
    NoopCaptureQueue captureQueue;
    NoopPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    //  Deterministic non-zero context A -- the same byte pattern (and
    //  matching C# GUID 00112233-4455-6677-8899-aabbccddeeff) the
    //  play-context wire test elsewhere in this file already uses --
    //  established through the normal AdapterPlayContextState API before
    //  either generation connects, so both G1 and G2 replay this same active
    //  context on authentication.
    const std::array<std::byte, 16> playContextId = {
        std::byte{0x00}, std::byte{0x11}, std::byte{0x22}, std::byte{0x33},
        std::byte{0x44}, std::byte{0x55}, std::byte{0x66}, std::byte{0x77},
        std::byte{0x88}, std::byte{0x99}, std::byte{0xAA}, std::byte{0xBB},
        std::byte{0xCC}, std::byte{0xDD}, std::byte{0xEE}, std::byte{0xFF}};
    playContextState.SetCurrentPlayContext(playContextId);
    std::unique_ptr<AdapterHostSupervisor> supervisor;
    AdapterIpcSession session(AdapterInstanceIdGenerator{}.Generate(),
                              ownerLifetimeId, taskMarshaller, captureRouter,
                              captureQueue, pairingNotificationSink,
                              playContextState);
    std::atomic<int> connectedCount = 0;
    //  Counts every IpcResynchronizeRequestMessage the real host sends, from
    //  the very first connection -- proof that G1's own active-context
    //  replay earns exactly one resync, and that RequestReconnect() below
    //  earns exactly one *additional* one, not merely "at least one" across
    //  both.
    std::atomic<int> resyncRequestCount = 0;
    AdapterIpcConnection connection(
        connectionSocket, codec,
        dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
            .onTargetConnected =
                [&](const AdapterIpcTarget& target) {
                    ++connectedCount;
                    session.HandleConnected(target);
                },
            .onMessageReceived =
                [&](const IpcMessage& message) {
                    if (std::holds_alternative<
                            dovahlink::adapter::ipc::IpcResynchronizeRequestMessage>(
                            message)) {
                        ++resyncRequestCount;
                    }
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

    //  G1: authenticate against the real host with active context A already
    //  established; AdapterIpcSession's own post-authentication replay (not
    //  this test) is what reports PlayContextChanged(A) to the host.
    REQUIRE(WaitUntil(
        [&] { return connectedCount.load() >= 1 && session.IsHostAvailable(); },
        std::chrono::seconds(10)));
    REQUIRE(IsProcessStillRunning(launcher.ProcessId()));

    //  The host's own reaction to that replayed active context is exactly one
    //  fresh resynchronization request -- proven before the reconnect below,
    //  so a duplicate here cannot later be misattributed to
    //  RequestReconnect() instead.
    REQUIRE(WaitUntil([&] { return resyncRequestCount.load() >= 1; },
                      std::chrono::seconds(10)));
    CHECK(resyncRequestCount.load() == 1);
    CHECK_FALSE(WaitUntil([&] { return resyncRequestCount.load() > 1; },
                          std::chrono::milliseconds(200)));
    const int resyncRequestCountAfterG1 = resyncRequestCount.load();

    //  Exactly what AdapterRuntime's onRejected callback does for a rejected
    //  reliable Event -- proven to be reached from that callback separately
    //  by adapter_runtime_test.cpp's own queue-rejection tests.
    connection.RequestReconnect();

    //  G2: the same still-running host is rediscovered (same rendezvous
    //  port) and a fresh authenticated generation is established -- proving
    //  RequestReconnect() does not permanently poison Start() the way Stop()
    //  would. AdapterPlayContextState still reports the SAME context A (it
    //  was never cleared or changed), so the normal replay path reports
    //  PlayContextChanged(A) again on this new generation.
    REQUIRE(WaitUntil(
        [&] { return connectedCount.load() >= 2 && session.IsHostAvailable(); },
        std::chrono::seconds(10)));
    CHECK(IsProcessStillRunning(launcher.ProcessId()));

    //  The reconnect's own replay of the SAME context A earns exactly one
    //  fresh resynchronization request beyond G1's -- proof that a reconnect
    //  re-establishes authoritative state even though Skyrim never left the
    //  same loaded play context, and that authentication plus context replay
    //  did not each independently trigger their own resync.
    REQUIRE(WaitUntil(
        [&] {
            return resyncRequestCount.load() >= resyncRequestCountAfterG1 + 1;
        },
        std::chrono::seconds(10)));
    CHECK(resyncRequestCount.load() == resyncRequestCountAfterG1 + 1);
    CHECK_FALSE(WaitUntil(
        [&] {
            return resyncRequestCount.load() > resyncRequestCountAfterG1 + 1;
        },
        std::chrono::milliseconds(200)));

    supervisor->RequestStop();
    connection.Stop();
    launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0));
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
        dovahlink::adapter::ipc::IAdapterPairingNotificationSink* pairingSink =
            nullptr,
        std::filesystem::path hostExecutable =
            std::filesystem::path(DOVAHLINK_HOST_EXECUTABLE))
        : hostExecutable_(std::move(hostExecutable)),
          ownerLifetimeId_(LifetimeIdWithMarker(ownerLifetimeMarker)),
          launcher_(hostExecutable_, ownerLifetimeId_, std::chrono::seconds(10)),
          session_(AdapterInstanceIdGenerator{}.Generate(), ownerLifetimeId_,
                   taskMarshaller_, captureRouter_, captureQueue_,
                   pairingSink != nullptr
                       ? *pairingSink
                       : static_cast<dovahlink::adapter::ipc::
                                         IAdapterPairingNotificationSink&>(
                             noopPairingNotificationSink_),
                   playContextState_),
          connection_(
              connectionSocket_, codec_,
              dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
                  .onTargetConnected =
                      [this](const AdapterIpcTarget& target) {
                          session_.HandleConnected(target);
                      },
                  .onMessageReceived =
                      [this](const IpcMessage& message) {
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

    RealHostFixture(const RealHostFixture&) = delete;
    RealHostFixture& operator=(const RealHostFixture&) = delete;

    ///  The authenticated session under test.
    AdapterIpcSession& Session() { return session_; }

    ///  The isolated trust-store file this fixture's real Host was launched
    ///  with, so a test can assert the Host actually persisted through it.
    const std::filesystem::path& TrustStorePath() const {
        return trustStorePath_.Path();
    }

  private:
    //  Declared first so it sets DOVAHLINK_TEST_TRUST_STORE_PATH before
    //  launcher_.Launch() runs in the constructor body below -- every real
    //  Host this fixture launches persists trust to this private file instead
    //  of the real per-Windows-user DPAPI-backed store.
    ScopedTestTrustStorePathEnvironmentVariable trustStorePath_;
    std::filesystem::path hostExecutable_;
    std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
        ownerLifetimeId_;
    Win32AdapterHostProcessLauncher launcher_;
    WinsockAdapterIpcSocket connectionSocket_{0};
    IpcFrameCodec codec_;
    ImmediateTaskMarshaller taskMarshaller_;
    AcceptingCaptureRouter captureRouter_;
    NoopCaptureQueue captureQueue_;
    NoopPairingNotificationSink noopPairingNotificationSink_;
    AdapterPlayContextState playContextState_;
    AdapterIpcSession session_;
    AdapterIpcConnection connection_;
};

///  Extends `RealHostFixture`'s real Hello/HelloAck proof with a real,
///  draining `AdapterCaptureHandoffQueue` (the production queue, not
///  `NoopCaptureQueue`) whose drain callback is wired to
///  `AdapterIpcSession::SendCaptureResult` exactly the way `AdapterRuntime`'s
///  own composition wires it (see `adapter/plugin/adapter_runtime.cpp`) --
///  so a captured baseline this fixture's `DeterministicBaselineCaptureRouter`
///  produces during a real Host-driven resynchronization actually crosses
///  the real private IPC connection, rather than being silently accepted and
///  dropped. Used only by the live-state E2E test below.
class RealHostLiveStateFixture {
  public:
    ///  Launches a real host under a fresh, uniquely marked owner-lifetime
    ///  id and a fixed public listener port, establishes `playContextId` as
    ///  the active play context before connecting, then connects and
    ///  authenticates a real native session against it -- the same
    ///  active-context-before-authentication ordering
    ///  `AdapterIpcSession::ReplayCurrentPlayContextState` requires for its
    ///  post-authentication replay to report it.
    RealHostLiveStateFixture(std::byte ownerLifetimeMarker,
                             std::array<std::byte, 16> playContextId,
                             std::uint16_t publicListenerPort,
                             dovahlink::adapter::ipc::IAdapterPairingNotificationSink&
                                 pairingSink)
        : publicListenerPort_(publicListenerPort),
          hostExecutable_(DOVAHLINK_HOST_EXECUTABLE),
          ownerLifetimeId_(LifetimeIdWithMarker(ownerLifetimeMarker)),
          launcher_(hostExecutable_, ownerLifetimeId_, std::chrono::seconds(10)),
          session_(AdapterInstanceIdGenerator{}.Generate(), ownerLifetimeId_,
                   taskMarshaller_, captureRouter_, captureQueue_, pairingSink,
                   playContextState_),
          connection_(
              connectionSocket_, codec_,
              dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
                  .onTargetConnected =
                      [this](const AdapterIpcTarget& target) {
                          session_.HandleConnected(target);
                      },
                  .onMessageReceived =
                      [this](const IpcMessage& message) {
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

        //  Deferred to the constructor body -- like `session_.AttachConnection`
        //  below -- rather than the initializer list, since `captureRouter_` is
        //  declared before `captureQueue_`/`playContextState_` and only needs
        //  to reach them once EmitLevelChanged is actually called, well after
        //  this constructor returns.
        captureRouter_.AttachLevelChangedEmitter(captureQueue_, playContextState_);

        //  Established before the private connection ever starts, so the
        //  normal post-authentication replay (not this test) reports it to
        //  the real Host, which then requests its own generic
        //  resynchronization plan.
        playContextState_.SetCurrentPlayContext(playContextId);

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

    ///  Stops the draining capture queue first -- joining its worker thread,
    ///  so no further drained item can ever call back into `session_` --
    ///  before any member destructor runs, since plain reverse-declaration-
    ///  order destruction would otherwise destroy `session_` while the
    ///  queue's worker thread could still be mid-drain.
    ~RealHostLiveStateFixture() {
        captureQueue_.Stop();
        connection_.Stop();
        launcher_.AwaitExitOrTerminate(std::chrono::seconds(5));
    }

    RealHostLiveStateFixture(const RealHostLiveStateFixture&) = delete;
    RealHostLiveStateFixture& operator=(const RealHostLiveStateFixture&) = delete;

    ///  @copydoc DeterministicBaselineCaptureRouter::IsLevelChangedRegistered
    bool IsLevelChangedRegistered() const {
        return captureRouter_.IsLevelChangedRegistered();
    }

    ///  @copydoc DeterministicBaselineCaptureRouter::EmitLevelChanged
    void EmitLevelChanged(std::uint16_t newLevel) {
        captureRouter_.EmitLevelChanged(newLevel);
    }

  private:
    //  Declared first so it sets DOVAHLINK_TEST_TRUST_STORE_PATH before
    //  launcher_.Launch() runs in the constructor body below, mirroring
    //  RealHostFixture's own ordering rationale.
    ScopedTestTrustStorePathEnvironmentVariable trustStorePath_;
    ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort_;
    std::filesystem::path hostExecutable_;
    std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
        ownerLifetimeId_;
    Win32AdapterHostProcessLauncher launcher_;
    WinsockAdapterIpcSocket connectionSocket_{0};
    IpcFrameCodec codec_;
    ImmediateTaskMarshaller taskMarshaller_;
    DeterministicBaselineCaptureRouter captureRouter_;
    //  The real production queue, draining onto SendCaptureResult exactly
    //  the way AdapterRuntime's own composition wires it -- captures `this`
    //  rather than `&session_` directly, since session_ is declared (and
    //  constructed) after this member, but the drain callback cannot run
    //  until this queue's worker thread actually drains an enqueued item,
    //  which happens well after this constructor finishes. Raises
    //  `TryEnqueue`'s own lock-contention retry budget well above the
    //  production default, and lets it yield between attempts: this
    //  fixture's caller is the IPC read thread via `ImmediateTaskMarshaller`,
    //  never the real Skyrim game thread the production default's
    //  never-yield contract protects, so it can afford to actually
    //  surrender its timeslice absorbing ordinary CI scheduling noise
    //  against this queue's own worker thread -- see the constructor
    //  parameters' own doc. Without this, a resync's baseline captures can
    //  spuriously fail to enqueue under scheduler noise, reporting the whole
    //  resynchronization declined and causing the Host to close the
    //  connection (AdapterIpcSession.HandleResynchronizeResult) before this
    //  test ever reaches its own assertions.
    AdapterCaptureHandoffQueue captureQueue_{
        [this](const AdapterCaptureWorkItem& item) {
            session_.SendCaptureResult(item);
        },
        [](const AdapterCaptureWorkItem&) {}, 64, true};
    AdapterPlayContextState playContextState_;
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
    //  same-process fake Host peer cannot close. RealHostFixture isolates the
    //  real Host's trust store to a private per-test file (see
    //  ScopedTestTrustStorePathEnvironmentVariable), which starts empty on
    //  every run, so this deliberately does not assert specific content --
    //  only that a well-formed, correctly correlated result decodes back.
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

TEST_CASE("a real native adapter completes a trust-admin Revoke request "
          "against a real launched Host, decoding its typed result",
          "[process][integration]") {
    //  Extends the trust-admin List E2E above to a short-id-targeted mutation:
    //  proves TrustAdminOperation::kRevoke and its shortId argument round-trip
    //  through the real cross-language wire encoding and a real Host's real
    //  handler, using the real native adapter binary. As with the List test
    //  above, this test's real Host runs against RealHostFixture's isolated
    //  per-test trust store, so no client with this short id is ever actually
    //  trusted here -- this asserts only that the result is well-formed and
    //  echoes the short id back, not a specific outcome.
    RealHostFixture fixture(std::byte{0xE8});

    auto resultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> resultFuture =
        resultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kRevoke, std::nullopt, std::string("99999"),
        std::nullopt, [resultPromise](TrustAdminRequestResult result) {
            resultPromise->set_value(std::move(result));
        });

    REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    REQUIRE(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    //  Matches every possible Revoke outcome (found-and-changed, not-found, or
    //  ineligible), never a Block/Unblock/Forget outcome that also happened to
    //  echo this short id -- a substring check alone could pass even if the
    //  real Host dispatched the wrong operation.
    CHECK(std::regex_search(
        *result.resultText,
        std::regex(
            R"(^(Revoked client 99999 \(.*\)\.|No trusted client with id 99999\.|Client 99999 cannot be revoked \(not currently trusted\)\.)$)")));
}

TEST_CASE("a real native adapter completes a trust-admin Block request "
          "against a real launched Host, decoding its typed result",
          "[process][integration]") {
    //  Mirrors the Revoke E2E above for TrustAdminOperation::kBlock.
    RealHostFixture fixture(std::byte{0xE9});

    auto resultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> resultFuture =
        resultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kBlock, std::nullopt, std::string("99999"),
        std::nullopt, [resultPromise](TrustAdminRequestResult result) {
            resultPromise->set_value(std::move(result));
        });

    REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    REQUIRE(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    //  Matches every possible Block outcome, never a Revoke/Unblock/Forget
    //  outcome that also happened to echo this short id -- see the Revoke E2E
    //  above for why a substring check alone is not dispatch-proof.
    CHECK(std::regex_search(
        *result.resultText,
        std::regex(
            R"(^(Blocked device 99999 \(.*\)\.|Device 99999 is already blocked\.|No known device with id 99999\.|Device 99999 cannot be blocked \(not currently trusted or revoked\)\.)$)")));
}

TEST_CASE("a real native adapter completes a trust-admin ResetTrust request "
          "against a real launched Host, decoding its typed result",
          "[process][integration]") {
    //  Mirrors the Revoke/Block E2Es above for the no-argument, bulk
    //  TrustAdminOperation::kResetTrust. Unlike Revoke/Block, ResetTrust's
    //  result text is deterministic regardless of the real trust store's
    //  content -- it always
    //  reports how many devices it revoked, including zero -- so this asserts
    //  the exact shape rather than merely a substring. Against RealHostFixture's
    //  isolated, empty-on-start per-test store, zero devices are ever trusted
    //  to revoke, so -- per TrustStore.ResetTrustAsync's own documented
    //  no-currently-trusted-record short circuit -- this real Host never writes
    //  its isolated trust-store file at all; the full-pairing/trusted-reconnect
    //  E2E below is where a real write through that isolated path is proven,
    //  since it is the one real Host operation in this file guaranteed to
    //  durably trust a device.
    RealHostFixture fixture(std::byte{0xEA});

    auto resultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> resultFuture =
        resultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kResetTrust, std::nullopt, std::nullopt,
        std::nullopt, [resultPromise](TrustAdminRequestResult result) {
            resultPromise->set_value(std::move(result));
        });

    REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    REQUIRE(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    CHECK(std::regex_search(
        *result.resultText,
        std::regex(R"(^Reset Trust complete \(\d+ devices? revoked\)\.$)")));
}

TEST_CASE("a real native adapter launches, authenticates against, and "
          "completes a real trust-admin request through the real Host "
          "resolved from the real assembled Vortex package layout",
          "[process][integration][package]") {
    //  Extends the List E2E above from a manually-built framework-dependent
    //  Host executable to the real installable artifact shape: CMakeLists.txt's
    //  AssembleRealAdapterHostPackage CTest fixture assembles the real
    //  Data/SKSE/Plugins/... layout via the real production packager
    //  (tooling/adapter_host_packager.py), and this resolves the Host
    //  executable from it the same way the real adapter plugin's own
    //  ResolveAdapterHostExecutablePath does -- combining
    //  kAdapterHostExecutableRelativePath with the plugin's own directory --
    //  rather than a path a test invented independently. A packager that puts
    //  the Host in the wrong directory, or a kAdapterHostExecutableRelativePath
    //  change the packager's real layout no longer matches, fails the
    //  REQUIRE below rather than silently launching the wrong file.
    std::filesystem::path pluginsDirectory{
        DOVAHLINK_ASSEMBLED_PACKAGE_PLUGINS_DIR};
    //  CTest's FIXTURES_REQUIRED only blocks this test when
    //  AssembleRealAdapterHostPackage genuinely fails, not when it uses
    //  SKIP_RETURN_CODE to skip -- so this test still runs even when the
    //  fixture skipped against a Debug build, and must tell that apart from a
    //  real layout bug itself: the plugins directory existing at all is proof
    //  the fixture actually ran and assembled something (it is created only
    //  once assemble_package's own input guards already passed), so its
    //  absence means "fixture skipped" (expected in Debug -- SKIP, not FAIL),
    //  while its presence without the resolved Host executable inside it means
    //  a genuine packaging/resolution bug (FAIL).
    if (!std::filesystem::exists(pluginsDirectory)) {
        SKIP("AssembleRealAdapterHostPackage's fixture did not assemble a "
             "package (no plugins directory at " +
             pluginsDirectory.string() +
             "), which is expected when this build's runtime DLLs are not "
             "Release-named -- see assemble_adapter_host_package_for_ctest.py.");
    }
    std::filesystem::path hostExecutable =
        pluginsDirectory / kAdapterHostExecutableRelativePath;
    REQUIRE(std::filesystem::exists(hostExecutable));

    //  RealHostFixture's constructor already proves the real Hello/HelloAck
    //  handshake completes (WaitUntil(IsHostAvailable)), under this same
    //  isolated per-test trust store every other real-process test in this
    //  file uses, and its destructor already proves graceful shutdown
    //  (AwaitExitOrTerminate) the same way every other fixture instance does.
    RealHostFixture fixture(std::byte{0xED}, /*pairingSink=*/nullptr,
                            hostExecutable);

    //  One real trust-admin round trip, proving the packaged binary actually
    //  serves real IPC requests -- not merely that a process started and
    //  produced a valid Hello/HelloAck.
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
        R"("unpaired"}},"playContextId":null,)"
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
        R"(","correlationId":null,"payload":{},)"
        R"("playContextId":null,"clientId":"e6e6e6e6-e6e6-e6e6-e6e6-e6e6e6e6e6e6"})");

    REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                      std::chrono::seconds(10)));
    auto displayed = pairingSink.Displayed();
    REQUIRE(displayed.size() == 1);
    const auto& [code, mode] = displayed.front();
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
        R"("unpaired"}},"playContextId":null,)"
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
        R"(","correlationId":null,"payload":{},)"
        R"("playContextId":null,"clientId":"e7e7e7e7-e7e7-e7e7-e7e7-e7e7e7e7e7e7"})");

    REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                      std::chrono::seconds(10)));
    CHECK(pairingSink.Displayed().size() == 1);

    std::string pairingStatus = client.ReceiveText();
    CHECK(pairingStatus.find(R"("messageType":"pairing_status")") !=
          std::string::npos);
    CHECK(pairingStatus.find(R"("state":"unavailable")") != std::string::npos);
}

TEST_CASE("a real native adapter completes full pairing and a fresh "
          "connection reconnects trusted with the issued credential, "
          "without repeating pairing",
          "[process][integration]") {
    //  Extends the pairing-display E2E above past the code display this file
    //  already proves, through the rest of the real cross-language pairing
    //  contract: the code the real native adapter displayed is submitted back
    //  as a real pairing_confirm, the real Host's real PairingCoordinator
    //  issues a real credential, a real pairing_ack durably trusts it, and a
    //  second, independent public connection presenting that exact credential
    //  is admitted as `clientIdentityKind: "paired"` with no adapter
    //  involvement at all -- proving trusted reconnect never depends on the
    //  adapter being present, using the real native adapter binary that
    //  produces the displayed code here.
    constexpr std::uint16_t kPublicListenerPort = 58431;
    ScopedTestPublicListenerPortEnvironmentVariable publicListenerPort(
        kPublicListenerPort);
    RecordingPairingNotificationSink pairingSink;
    RealHostFixture fixture(std::byte{0xEB}, &pairingSink);
    const std::string clientId = "ebebebeb-ebeb-ebeb-ebeb-ebebebebebeb";

    std::string sessionId;
    std::string code;
    std::string credential;
    {
        //  Scoped so the first client's socket -- and its admitted session --
        //  closes before the reconnect below opens a second, independent
        //  connection, proving reconnect never resumes or reuses this one.
        MinimalPublicWebSocketClient client(kPublicListenerPort);
        client.SendText(
            R"({"messageType":"hello","messageId":"m1","sessionId":null,)"
            R"("correlationId":null,"payload":{"endpoint":"client",)"
            R"("clientId":")" +
            clientId +
            R"(","auth":{"method":"unpaired"}},)"
            R"("playContextId":null,"clientId":null})");
        std::string helloAck = client.ReceiveText();
        REQUIRE(helloAck.find(R"("messageType":"hello_ack")") != std::string::npos);
        sessionId = ExtractJsonStringField(helloAck, "sessionId");
        REQUIRE_FALSE(sessionId.empty());
        std::string capabilities = client.ReceiveText();
        REQUIRE(capabilities.find(R"("messageType":"capabilities")") !=
                std::string::npos);

        client.SendText(
            R"({"messageType":"pairing_request","messageId":"m2","sessionId":")" +
            sessionId + R"(","correlationId":null,"payload":{},)" +
            R"("playContextId":null,"clientId":")" +
            clientId + R"("})");

        REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                          std::chrono::seconds(10)));
        auto displayed = pairingSink.Displayed();
        REQUIRE(displayed.size() == 1);
        code = displayed.front().first;
        REQUIRE_FALSE(code.empty());

        std::string pairingStatus = client.ReceiveText();
        REQUIRE(pairingStatus.find(R"("state":"available")") != std::string::npos);

        //  The code a human would read off their Skyrim screen -- never sent
        //  over the public wire -- is exactly what the real native adapter was
        //  just told to display.
        client.SendText(
            R"({"messageType":"pairing_confirm","messageId":"m3","sessionId":")" +
            sessionId + R"(","correlationId":null,"payload":{"code":")" + code +
            R"(","displayName":"Native E2E PC"},)" +
            R"("playContextId":null,"clientId":")" + clientId + R"("})");
        std::string confirmOutcome = client.ReceiveText();
        REQUIRE(confirmOutcome.find(R"("messageType":"pairing_outcome")") !=
                std::string::npos);
        REQUIRE(confirmOutcome.find(R"("outcome":"credential_issued")") !=
                std::string::npos);
        credential = ExtractJsonStringField(confirmOutcome, "credential");
        REQUIRE_FALSE(credential.empty());

        client.SendText(
            R"({"messageType":"pairing_ack","messageId":"m4","sessionId":")" +
            sessionId + R"(","correlationId":null,"payload":{"credential":")" +
            credential + R"("},"playContextId":null,)" +
            R"("clientId":")" + clientId + R"("})");
        std::string ackOutcome = client.ReceiveText();
        REQUIRE(ackOutcome.find(R"("messageType":"pairing_outcome")") !=
                std::string::npos);
        REQUIRE(ackOutcome.find(R"("outcome":"trusted")") != std::string::npos);
        REQUIRE(ExtractJsonStringField(ackOutcome, "credential") == credential);
        //  This first-time pairing durably trusts a device that was previously
        //  unknown, so -- unlike ResetTrust/Revoke/Block above against this same
        //  fixture's isolated, empty-on-start store -- it unconditionally writes
        //  through persistence. Proves the real Host actually wrote through
        //  RealHostFixture's isolated per-test file, not merely that
        //  DOVAHLINK_TEST_TRUST_STORE_PATH was set: closes the loop on the trust-
        //  store isolation this whole file depends on.
        CHECK(std::filesystem::exists(fixture.TrustStorePath()));
    }

    //  A fresh connection presenting the exact credential just issued must be
    //  admitted directly as trusted -- proving reconnect never requires
    //  repeating the pairing flow above, or the adapter's own display/ack
    //  round-trip -- opened only now that the first client (and its own
    //  admitted session) has already gone out of scope above. A raw TCP
    //  connect immediately following that first client's abrupt closesocket()
    //  (no graceful WebSocket close handshake, unlike a real SDK client) can
    //  briefly race the real Host's own accept-loop cleanup for the closed
    //  connection; retried bounded rather than treated as a hard failure,
    //  mirroring this file's own WaitUntil idiom for other bounded async
    //  waits.
    std::unique_ptr<MinimalPublicWebSocketClient> reconnectedClientPtr;
    for (int attempt = 0; attempt < 20 && reconnectedClientPtr == nullptr;
         ++attempt) {
        try {
            reconnectedClientPtr =
                std::make_unique<MinimalPublicWebSocketClient>(kPublicListenerPort);
        } catch (const std::exception&) {
            std::this_thread::sleep_for(std::chrono::milliseconds(250));
        }
    }
    REQUIRE(reconnectedClientPtr != nullptr);
    MinimalPublicWebSocketClient& reconnectedClient = *reconnectedClientPtr;
    reconnectedClient.SendText(
        R"({"messageType":"hello","messageId":"hello-reconnect-1",)"
        R"("sessionId":null,"correlationId":null,"payload":{"endpoint":)"
        R"("client","clientId":")" +
        clientId + R"(","auth":{"method":"trusted_device_credential","token":")" +
        credential +
        R"("}},"playContextId":null,"clientId":)"
        R"(null})");
    std::string reconnectHelloAck = reconnectedClient.ReceiveText();
    REQUIRE(reconnectHelloAck.find(R"("messageType":"hello_ack")") !=
            std::string::npos);
    CHECK(reconnectHelloAck.find(R"("clientIdentityKind":"paired")") !=
          std::string::npos);
    //  A trusted reconnect is a fresh session, not a resumed one.
    CHECK(ExtractJsonStringField(reconnectHelloAck, "sessionId") != sessionId);
    std::string reconnectCapabilities = reconnectedClient.ReceiveText();
    CHECK(reconnectCapabilities.find(R"("messageType":"capabilities")") !=
          std::string::npos);
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

TEST_CASE("a real native adapter's capture result is accepted by a real "
          "launched Host without closing the connection",
          "[process][integration]") {
    //  Proves the real adapter-to-host wire encoding for
    //  IpcCaptureResultMessage and that a real Host accepts it and keeps
    //  serving the connection, even though no live capture sink is wired in
    //  yet on the Host side (see AdapterIpcSession::HandleFrame's own
    //  documentation there).
    RealHostFixture fixture(std::byte{0xF1});

    fixture.Session().SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 1,
        .capturedValue = {},
        .correlationId = 0,
        .source = CaptureSourceKind::kSample,
        .availability = CaptureAvailability::kUnavailable,
    });

    //  The connection stays open and authenticated: a follow-up trust-admin
    //  request still completes normally.
    auto resultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> resultFuture =
        resultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [resultPromise](TrustAdminRequestResult result) {
            resultPromise->set_value(std::move(result));
        });

    REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kCompleted);
}

TEST_CASE("a real native adapter's listen-event registration reply is "
          "accepted by a real launched Host without closing the connection",
          "[process][integration]") {
    //  Proves the real adapter-to-host wire encoding for
    //  IpcListenEventResultMessage and that a real Host accepts it and keeps
    //  serving the connection, even though no production caller of
    //  PrepareListenEvent exists yet on the Host side (see
    //  AdapterIpcSession::HandleFrame's own documentation there).
    RealHostFixture fixture(std::byte{0xF2});

    fixture.Session().HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 1}});

    //  The connection stays open and authenticated: a follow-up trust-admin
    //  request still completes normally.
    auto resultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> resultFuture =
        resultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [resultPromise](TrustAdminRequestResult result) {
            resultPromise->set_value(std::move(result));
        });

    REQUIRE(resultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kCompleted);
}

TEST_CASE("a real native adapter's play-context-changed notification is "
          "accepted by a real launched Host without closing the connection",
          "[process][integration]") {
    //  Proves the real adapter-to-host wire encoding for
    //  IpcPlayContextChangedMessage and that a real Host accepts it and keeps
    //  serving the connection. HandleFrame's real IPlayContextTracker wiring
    //  (rather than only accepting the frame) is proven at the host-side
    //  unit level in AdapterIpcSessionTests.cs, since this fixture has no
    //  wire-level way to query the real Host's internal tracker state.
    RealHostFixture fixture(std::byte{0xF3});

    fixture.Session().SendPlayContextChanged(
        {std::byte{0x00}, std::byte{0x11}, std::byte{0x22}, std::byte{0x33},
         std::byte{0x44}, std::byte{0x55}, std::byte{0x66}, std::byte{0x77},
         std::byte{0x88}, std::byte{0x99}, std::byte{0xAA}, std::byte{0xBB},
         std::byte{0xCC}, std::byte{0xDD}, std::byte{0xEE}, std::byte{0xFF}});

    //  The connection stays open and authenticated: a follow-up trust-admin
    //  request still completes normally.
    auto secondResultPromise =
        std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> secondResultFuture =
        secondResultPromise->get_future();
    fixture.Session().SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [secondResultPromise](TrustAdminRequestResult result) {
            secondResultPromise->set_value(std::move(result));
        });

    REQUIRE(secondResultFuture.wait_for(std::chrono::seconds(10)) ==
            std::future_status::ready);
    CHECK(secondResultFuture.get().outcome ==
          TrustAdminRequestOutcome::kCompleted);
}

TEST_CASE("a real native adapter's Host-driven resynchronization baseline "
          "reaches a real public WebSocket client as a character_xp "
          "Snapshot",
          "[process][integration]") {
    //  The end-to-end live-state proof: a synthetic native capture in this
    //  real Adapter test process crosses the real AdapterIpcSession, the
    //  real AdapterCaptureHandoffQueue, the real private IPC connection, a
    //  real launched Host process, its real LiveCaptureSink/
    //  CharacterCaptureHandler/LiveStateApplication/StatePublisher/
    //  publication feed, and finally the real public WebSocket transport --
    //  observed here only through a real public client, never by inspecting
    //  Host-internal services directly. XP = 42.5 travels only because
    //  DeterministicBaselineCaptureRouter reported it from the real
    //  Host-driven resynchronization plan this fixture never asks for
    //  directly: the normal active-context replay below is what earns it.
    constexpr std::uint16_t kPublicListenerPort = 58432;
    const std::array<std::byte, 16> playContextId = {
        std::byte{0xF9}, std::byte{0xE8}, std::byte{0xD7}, std::byte{0xC6},
        std::byte{0xB5}, std::byte{0xA4}, std::byte{0x93}, std::byte{0x82},
        std::byte{0x71}, std::byte{0x60}, std::byte{0x5F}, std::byte{0x4E},
        std::byte{0x3D}, std::byte{0x2C}, std::byte{0x1B}, std::byte{0x0A}};
    RecordingPairingNotificationSink pairingSink;
    RealHostLiveStateFixture fixture(std::byte{0xF9}, playContextId,
                                     kPublicListenerPort, pairingSink);

    //  The real Host's own reaction to the real native adapter's replayed
    //  active context: a fresh resynchronization request against the
    //  current catalog-derived plan, executed by the real
    //  AdapterIpcSession -- not asked for by this test.
    const std::string clientId = "f9f9f9f9-f9f9-f9f9-f9f9-f9f9f9f9f9f9";
    MinimalPublicWebSocketClient client(kPublicListenerPort);
    client.SendText(
        R"({"messageType":"hello","messageId":"m1","sessionId":null,)"
        R"("correlationId":null,"payload":{"endpoint":"client",)"
        R"("clientId":")" +
        clientId +
        R"(","auth":{"method":"unpaired"}},)"
        R"("playContextId":null,"clientId":null})");
    std::string helloAck = client.ReceiveText();
    REQUIRE(helloAck.find(R"("messageType":"hello_ack")") != std::string::npos);
    std::string sessionId = ExtractJsonStringField(helloAck, "sessionId");
    REQUIRE_FALSE(sessionId.empty());
    std::string capabilities = client.ReceiveText();
    REQUIRE(capabilities.find(R"("messageType":"capabilities")") !=
            std::string::npos);

    //  A Restricted-tier session may not subscribe -- only Full trust may
    //  (see PublicHelloAdmissionHandler::IsAllowedForTier) -- so this client
    //  must complete real pairing first, exactly like the pairing E2Es
    //  above, reusing that same real cross-language pairing/trust flow
    //  rather than a test-only bypass. UpgradeToFullTrust upgrades this same
    //  session's tier in place once pairing_ack resolves, so no reconnect is
    //  needed before subscribing below.
    client.SendText(
        R"({"messageType":"pairing_request","messageId":"m2","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{},)" +
        R"("playContextId":null,"clientId":")" + clientId + R"("})");
    REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                      std::chrono::seconds(10)));
    auto displayed = pairingSink.Displayed();
    REQUIRE(displayed.size() == 1);
    const std::string code = displayed.front().first;
    REQUIRE_FALSE(code.empty());
    std::string pairingStatus = client.ReceiveText();
    REQUIRE(pairingStatus.find(R"("state":"available")") != std::string::npos);

    client.SendText(
        R"({"messageType":"pairing_confirm","messageId":"m3","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{"code":")" + code +
        R"(","displayName":"Live-State E2E PC"},)" +
        R"("playContextId":null,"clientId":")" + clientId + R"("})");
    std::string confirmOutcome = client.ReceiveText();
    REQUIRE(confirmOutcome.find(R"("outcome":"credential_issued")") !=
            std::string::npos);
    std::string credential = ExtractJsonStringField(confirmOutcome, "credential");
    REQUIRE_FALSE(credential.empty());

    client.SendText(
        R"({"messageType":"pairing_ack","messageId":"m4","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{"credential":")" +
        credential + R"("},"playContextId":null,)" + R"("clientId":")" +
        clientId + R"("})");
    std::string ackOutcome = client.ReceiveText();
    REQUIRE(ackOutcome.find(R"("outcome":"trusted")") != std::string::npos);

    //  Full trust now: subscribe to exactly the one state area this test
    //  cares about. subscription_ack accepts it immediately -- registration
    //  (RegisteredStateAreaPolicy) is independent of whether a baseline
    //  value is available yet -- so acceptance alone does not prove the
    //  resynchronization baseline arrived; the state_snapshot below does.
    client.SendText(
        R"({"messageType":"subscribe","messageId":"m5","sessionId":")" +
        sessionId +
        R"(","correlationId":null,"payload":{"stateAreas":["character_xp"]},)"
        R"("playContextId":null,"clientId":")" +
        clientId + R"("})");
    std::string subscriptionAck = client.ReceiveText();
    REQUIRE(subscriptionAck.find(R"("messageType":"subscription_ack")") !=
            std::string::npos);
    REQUIRE(subscriptionAck.find(R"("acceptedStateAreas":["character_xp"])") !=
            std::string::npos);

    //  The Host's own pending-baseline mechanism (Constants.PendingBaselineDeadline
    //  = 5s) answers this subscribe with the baseline state_snapshot once the
    //  real resynchronization -- already in flight from this adapter's own
    //  active-context replay above -- actually completes; no manual trigger.
    //  A well-formed hello_ack/capabilities/subscription_ack may legitimately
    //  precede it, so this waits for the specific expected message type
    //  rather than assuming the very next frame.
    std::string snapshot =
        ReceiveUntil(client, "state_snapshot", std::chrono::seconds(15));

    //  Proves this is specifically the character_xp Snapshot, not merely a
    //  frame containing the substring "42.5" somewhere.
    CHECK(ExtractJsonStringField(snapshot, "stateArea") == "character_xp");
    CHECK(ExtractJsonStringField(snapshot, "correlationId") == "m5");
    std::optional<double> revision = ExtractJsonNumberField(snapshot, "revision");
    REQUIRE(revision.has_value());
    CHECK(*revision >= 1.0);
    //  42.5 is exactly representable in both float and double, so this
    //  compares exactly rather than needing an epsilon.
    std::optional<double> value = ExtractJsonNumberField(snapshot, "value");
    REQUIRE(value.has_value());
    CHECK(*value == 42.5);

    //  The active play context this adapter replayed is the one the Host
    //  captured the baseline under.
    std::string expectedPlayContextId =
        "f9e8d7c6-b5a4-9382-7160-5f4e3d2c1b0a";
    CHECK(ExtractJsonStringField(snapshot, "playContextId") ==
          expectedPlayContextId);
}

TEST_CASE("a synthetic native LevelChanged event reaches a real public "
          "WebSocket client as a character_level state_event, distinct from "
          "its own Sample baseline Snapshot",
          "[process][integration]") {
    //  The second half of the live-state E2E proof: this test's synthetic
    //  native LevelChanged callback (DeterministicBaselineCaptureRouter::
    //  EmitLevelChanged, mirroring the real
    //  CommonLibAdapterNativeCaptureRouter::LevelChangedEventSink) crosses
    //  the same real AdapterCaptureHandoffQueue, private IPC connection,
    //  launched Host process, and public WebSocket transport the XP E2E
    //  above already proves for a Sample -- but for the Event path: Level's
    //  baseline (source = Sample) publishes as a Snapshot, while the later
    //  LevelChanged (source = Event) publishes as a state_event, per
    //  CharacterCaptureHandler::ApplyLevel's explicit source-to-mode split.
    constexpr std::uint16_t kPublicListenerPort = 58433;
    const std::array<std::byte, 16> playContextId = {
        std::byte{0xAB}, std::byte{0xCD}, std::byte{0xEF}, std::byte{0x01},
        std::byte{0x23}, std::byte{0x45}, std::byte{0x67}, std::byte{0x89},
        std::byte{0xAB}, std::byte{0xCD}, std::byte{0xEF}, std::byte{0x01},
        std::byte{0x23}, std::byte{0x45}, std::byte{0x67}, std::byte{0x89}};
    RecordingPairingNotificationSink pairingSink;
    RealHostLiveStateFixture fixture(std::byte{0xAB}, playContextId,
                                     kPublicListenerPort, pairingSink);

    //  The real Host's own reaction to the real native adapter's replayed
    //  active context: a fresh resynchronization request against the
    //  current catalog-derived plan, executed by the real
    //  AdapterIpcSession -- not asked for by this test. This is also what
    //  actually calls RegisterEvent(CharacterLevelChanged) on the router.
    const std::string clientId = "abababab-abab-abab-abab-abababababab";
    MinimalPublicWebSocketClient client(kPublicListenerPort);
    client.SendText(
        R"({"messageType":"hello","messageId":"m1","sessionId":null,)"
        R"("correlationId":null,"payload":{"endpoint":"client",)"
        R"("clientId":")" +
        clientId +
        R"(","auth":{"method":"unpaired"}},)"
        R"("playContextId":null,"clientId":null})");
    std::string helloAck = client.ReceiveText();
    REQUIRE(helloAck.find(R"("messageType":"hello_ack")") != std::string::npos);
    std::string sessionId = ExtractJsonStringField(helloAck, "sessionId");
    REQUIRE_FALSE(sessionId.empty());
    std::string capabilities = client.ReceiveText();
    REQUIRE(capabilities.find(R"("messageType":"capabilities")") !=
            std::string::npos);

    //  A Restricted-tier session may not subscribe -- only Full trust may --
    //  so this client must complete real pairing first, reusing the same
    //  real cross-language pairing/trust flow as the XP E2E above.
    client.SendText(
        R"({"messageType":"pairing_request","messageId":"m2","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{},)" +
        R"("playContextId":null,"clientId":")" + clientId + R"("})");
    REQUIRE(WaitUntil([&] { return !pairingSink.Displayed().empty(); },
                      std::chrono::seconds(10)));
    auto displayed = pairingSink.Displayed();
    REQUIRE(displayed.size() == 1);
    const std::string code = displayed.front().first;
    REQUIRE_FALSE(code.empty());
    std::string pairingStatus = client.ReceiveText();
    REQUIRE(pairingStatus.find(R"("state":"available")") != std::string::npos);

    client.SendText(
        R"({"messageType":"pairing_confirm","messageId":"m3","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{"code":")" + code +
        R"(","displayName":"Level Event E2E PC"},)" +
        R"("playContextId":null,"clientId":")" + clientId + R"("})");
    std::string confirmOutcome = client.ReceiveText();
    REQUIRE(confirmOutcome.find(R"("outcome":"credential_issued")") !=
            std::string::npos);
    std::string credential = ExtractJsonStringField(confirmOutcome, "credential");
    REQUIRE_FALSE(credential.empty());

    client.SendText(
        R"({"messageType":"pairing_ack","messageId":"m4","sessionId":")" +
        sessionId + R"(","correlationId":null,"payload":{"credential":")" +
        credential + R"("},"playContextId":null,)" + R"("clientId":")" +
        clientId + R"("})");
    std::string ackOutcome = client.ReceiveText();
    REQUIRE(ackOutcome.find(R"("outcome":"trusted")") != std::string::npos);

    //  Full trust now: subscribe to exactly the one state area this test
    //  cares about.
    client.SendText(
        R"({"messageType":"subscribe","messageId":"m5","sessionId":")" +
        sessionId +
        R"(","correlationId":null,"payload":{"stateAreas":["character_level"]},)"
        R"("playContextId":null,"clientId":")" +
        clientId + R"("})");
    std::string subscriptionAck = client.ReceiveText();
    REQUIRE(subscriptionAck.find(R"("messageType":"subscription_ack")") !=
            std::string::npos);
    REQUIRE(subscriptionAck.find(R"("acceptedStateAreas":["character_level"])") !=
            std::string::npos);

    //  The Host's own pending-baseline mechanism answers this subscribe with
    //  the baseline state_snapshot once the real resynchronization --
    //  already in flight from this adapter's own active-context replay
    //  above -- actually completes; no manual trigger.
    std::string snapshot =
        ReceiveUntil(client, "state_snapshot", std::chrono::seconds(15));

    //  Proves this is specifically the character_level baseline Snapshot
    //  (source = Sample), not merely a frame containing "10" somewhere, and
    //  captures its revision to compare the later Event's revision against.
    CHECK(ExtractJsonStringField(snapshot, "stateArea") == "character_level");
    CHECK(ExtractJsonStringField(snapshot, "correlationId") == "m5");
    std::optional<double> baselineRevision =
        ExtractJsonNumberField(snapshot, "revision");
    REQUIRE(baselineRevision.has_value());
    CHECK(*baselineRevision >= 1.0);
    std::optional<double> baselineValue = ExtractJsonNumberField(snapshot, "value");
    REQUIRE(baselineValue.has_value());
    CHECK(*baselineValue == 10.0);
    std::string expectedPlayContextId =
        "abcdef01-2345-6789-abcd-ef0123456789";
    CHECK(ExtractJsonStringField(snapshot, "playContextId") ==
          expectedPlayContextId);

    //  Registration must matter: this test's own EmitLevelChanged below must
    //  enter through a real Host-driven RegisterEvent(CharacterLevelChanged)
    //  call, not fire unconditionally. The baseline Snapshot above already
    //  implies the same resynchronization round completed, but this proves
    //  the registration explicitly rather than assuming it.
    REQUIRE(WaitUntil([&] { return fixture.IsLevelChangedRegistered(); },
                      std::chrono::seconds(10)));

    //  Only now -- registration confirmed and the Sample baseline already
    //  observed -- simulate the real native LevelChanged callback. This
    //  enters only through the router/native-event boundary: the real
    //  AdapterCaptureHandoffQueue, the real AdapterIpcSession::
    //  SendCaptureResult, the real private IPC connection, and the real
    //  Host's CharacterCaptureHandler Event path, exactly like the real
    //  RE::LevelIncrease::Event sink would.
    fixture.EmitLevelChanged(11);

    //  No other subscribed area can ever produce a state_event on this
    //  connection, so matching on messageType alone is already unambiguous
    //  proof this is the Level Event, not another Snapshot.
    std::string event = ReceiveUntil(client, "state_event", std::chrono::seconds(15));

    CHECK(ExtractJsonStringField(event, "stateArea") == "character_level");
    //  Unsolicited: a real Event is never a reply to a specific client
    //  request, unlike the correlated baseline Snapshot above.
    CHECK(ExtractJsonStringField(event, "correlationId").empty());
    std::optional<double> baseRevision =
        ExtractJsonNumberField(event, "baseRevision");
    REQUIRE(baseRevision.has_value());
    CHECK(*baseRevision == *baselineRevision);
    std::optional<double> eventRevision = ExtractJsonNumberField(event, "revision");
    REQUIRE(eventRevision.has_value());
    CHECK(*eventRevision > *baselineRevision);
    std::optional<double> eventValue = ExtractJsonNumberField(event, "value");
    REQUIRE(eventValue.has_value());
    CHECK(*eventValue == 11.0);
    CHECK(ExtractJsonStringField(event, "playContextId") ==
          expectedPlayContextId);
}
