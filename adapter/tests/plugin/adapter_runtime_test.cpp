#include "plugin/adapter_runtime.hpp"

#include "ipc/adapter_task_marshaller_test_support.hpp"
#include "ipc/ipc_frame_codec.hpp"

#include <catch2/catch_test_macros.hpp>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <memory>
#include <span>
#include <stdexcept>
#include <string>

using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::CaptureAvailability;
using dovahlink::adapter::capture::CaptureSourceKind;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::dispatch::AdapterNativeCaptureRouter;
using dovahlink::adapter::dispatch::IAdapterNativeCaptureRouter;
using dovahlink::adapter::identity::IAdapterPlayContextState;
using dovahlink::adapter::ipc::IAdapterPairingNotificationSink;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::PairingDisplayMode;
using dovahlink::adapter::ipc::test_support::FakeAdapterTaskMarshaller;
using dovahlink::adapter::plugin::AdapterRuntime;
using dovahlink::adapter::plugin::AdapterStartupContext;

namespace {

///  A no-op `IAdapterPairingNotificationSink` that only records whether it
///  was ever invoked, for tests that only need to prove the sink AdapterRuntime
///  was constructed with is the one actually wired into the session -- not a
///  full pairing-flow fake.
class RecordingPairingNotificationSink final
    : public IAdapterPairingNotificationSink {
  public:
    bool Display(const std::string&, PairingDisplayMode) override {
        ++displayCalls_;
        return true;
    }

    void NotifyAttemptsExhausted() override {}

    int DisplayCalls() const { return displayCalls_; }

  private:
    int displayCalls_ = 0;
};

///  A capture-router factory that always builds the core no-op stub, for
///  tests that only need AdapterRuntime's own wiring to compile and run --
///  not real native capture behavior, which requires CommonLib and is
///  covered separately by the runtime/ structural tests.
std::unique_ptr<IAdapterNativeCaptureRouter>
MakeStubCaptureRouter(IAdapterCaptureHandoffQueue&, IAdapterPlayContextState&) {
    return std::make_unique<AdapterNativeCaptureRouter>();
}

///  Builds a startup context pointing at safe, non-colliding scratch paths:
///  a rendezvous file that does not exist (so discovery's first read returns
///  nothing rather than blocking) and a host executable path that does not
///  exist (so a launch attempt fails immediately instead of actually
///  starting a process).
AdapterStartupContext BuildStartupContext(
    const std::filesystem::path& scratchDirectory) {
    return AdapterStartupContext{
        .instanceId = {.value = {std::byte{7}}},
        .ownerLifetimeId = {std::byte{1}, std::byte{2}, std::byte{3}},
        .rendezvousPath = scratchDirectory / "no-such-rendezvous.json",
        .hostExecutablePath = scratchDirectory / "no-such-host.exe",
    };
}

///  A minimal raw TCP loopback listener, standing in for a real host so
///  AdapterRuntime's own connection worker thread can be proven against an
///  actual live socket, per ai/context/skse/testing.md's "before using real
///  sockets" -- AdapterRuntime has no injectable socket seam to fake this
///  with instead.
class LoopbackListener {
  public:
    LoopbackListener() {
        WSADATA wsaData;
        WSAStartup(MAKEWORD(2, 2), &wsaData);

        listenSocket_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        REQUIRE(listenSocket_ != INVALID_SOCKET);

        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_port = 0;
        inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
        REQUIRE(bind(listenSocket_, reinterpret_cast<sockaddr*>(&address),
                     sizeof(address)) == 0);
        REQUIRE(listen(listenSocket_, 1) == 0);

        int addressLength = sizeof(address);
        REQUIRE(getsockname(listenSocket_, reinterpret_cast<sockaddr*>(&address),
                            &addressLength) == 0);
        port_ = ntohs(address.sin_port);
    }

    ~LoopbackListener() {
        if (acceptedSocket_ != INVALID_SOCKET) {
            closesocket(acceptedSocket_);
        }
        if (listenSocket_ != INVALID_SOCKET) {
            closesocket(listenSocket_);
        }
        WSACleanup();
    }

    LoopbackListener(const LoopbackListener&) = delete;
    LoopbackListener& operator=(const LoopbackListener&) = delete;

    ///  The actual loopback port this listener is bound to.
    std::uint16_t Port() const { return port_; }

    ///  The socket accepted by the most recent `AcceptOne()` call. Valid only
    ///  after `AcceptOne()` has returned.
    SOCKET AcceptedSocket() const { return acceptedSocket_; }

    ///  Drains and discards exactly one complete IPC frame from the accepted
    ///  socket -- the Hello the adapter sends immediately upon connecting,
    ///  before this listener ever replies -- so a later close-detection read
    ///  is never mistaken for leftover handshake bytes still sitting in the
    ///  receive buffer, and never itself consumes bytes belonging to
    ///  whatever the caller sends next. TCP gives no guarantee the whole
    ///  frame arrives in one `recv()`, so this reads the length prefix and
    ///  payload each via bounded, poll-gated reads rather than a single
    ///  best-effort one.
    void DrainAvailableBytes() {
        std::array<std::byte, sizeof(std::uint32_t)> lengthPrefix{};
        ReadExactly(lengthPrefix);

        IpcFrameCodec codec;
        std::optional<std::size_t> frameLength =
            codec.TryReadFrameLength(lengthPrefix);
        REQUIRE(frameLength.has_value());

        std::vector<std::byte> frame(*frameLength);
        ReadExactly(frame);
    }

    ///  Blocks, bounded, until one connection is accepted -- the
    ///  deterministic barrier proving AdapterRuntime's own connection worker
    ///  thread has physically connected and moved on to waiting for the
    ///  Hello handshake it will never receive from this listener. Polls for
    ///  readability first and only then calls the now-guaranteed-non-blocking
    ///  `accept()`, entirely on this thread: Winsock's own `closesocket`
    ///  documentation ("a Winsock client must never issue closesocket on `s`
    ///  concurrently with another Winsock function call") rules out the
    ///  alternative of accepting on a background thread and closing the
    ///  listening socket from this one to interrupt a timed-out wait.
    void AcceptOne() {
        WSAPOLLFD pollFd{.fd = listenSocket_, .events = POLLRDNORM};
        int pollResult = WSAPoll(&pollFd, 1, 5000);
        REQUIRE(pollResult > 0);
        REQUIRE((pollFd.revents & POLLRDNORM) != 0);

        acceptedSocket_ = accept(listenSocket_, nullptr, nullptr);
        REQUIRE(acceptedSocket_ != INVALID_SOCKET);
    }

  private:
    ///  Blocks, bounded, until `buffer` is completely filled from the
    ///  accepted socket -- polling before each `recv()` so a TCP segment
    ///  that lands short of the whole frame is read to completion instead of
    ///  being mistaken for it.
    void ReadExactly(std::span<std::byte> buffer) {
        std::size_t totalRead = 0;
        while (totalRead < buffer.size()) {
            WSAPOLLFD pollFd{.fd = acceptedSocket_, .events = POLLRDNORM};
            REQUIRE(WSAPoll(&pollFd, 1, 5000) > 0);
            int received =
                recv(acceptedSocket_,
                     reinterpret_cast<char*>(buffer.data()) + totalRead,
                     static_cast<int>(buffer.size() - totalRead), 0);
            REQUIRE(received > 0);
            totalRead += static_cast<std::size_t>(received);
        }
    }

    SOCKET listenSocket_ = INVALID_SOCKET;
    SOCKET acceptedSocket_ = INVALID_SOCKET;
    std::uint16_t port_ = 0;
};

///  Writes a rendezvous file `FileAdapterHostRendezvousReader` can parse,
///  pointing at `port` with well-formed but arbitrary proof bytes -- this
///  test never completes real authentication, only needs
///  `AdapterHostSupervisor`'s discovery round to adopt this candidate and
///  start the connection against a real, live socket.
void WriteRendezvousFile(const std::filesystem::path& path,
                         std::uint16_t port) {
    std::ofstream file(path, std::ios::binary);
    file << "PORT " << port << "\nPROOF a0b1c2\nHOSTPROOF d3e4\n";
}

} //  namespace

TEST_CASE("AdapterRuntime constructs its complete object graph exactly once "
          "without throwing, and exposes the session it wired the supplied "
          "collaborators into") {
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    AdapterRuntime runtime(BuildStartupContext(scratchDirectory), taskMarshaller, pairingSink, MakeStubCaptureRouter, [](const AdapterCaptureWorkItem&) {}, [](const AdapterCaptureWorkItem&) {}, [] {});

    //  A freshly constructed session has no connected host yet; observing this
    //  through the real session confirms Session() returns the same object the
    //  constructor wired, not a placeholder.
    CHECK_FALSE(runtime.Session().IsHostAvailable());
    //  Session() is an accessor onto the one session AdapterRuntime owns, not a
    //  factory -- repeated calls must return the same instance.
    CHECK(&runtime.Session() == &runtime.Session());
}

TEST_CASE("AdapterRuntime::Start is idempotent and destruction while started "
          "does not hang") {
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    AdapterRuntime runtime(BuildStartupContext(scratchDirectory), taskMarshaller, pairingSink, MakeStubCaptureRouter, [](const AdapterCaptureWorkItem&) {}, [](const AdapterCaptureWorkItem&) {}, [] {});

    runtime.Start();
    runtime.Start();
    //  `runtime`'s destructor runs here, at scope exit, while discovery may
    //  still be running against the nonexistent rendezvous/host paths above.
    //  A hang here would fail this test's own execution rather than an
    //  assertion -- the proof is that this test case completes at all.
}

TEST_CASE("AdapterRuntime destroys safely while its connection is actively "
          "serving a live socket") {
    //  Unlike the tests above, this one gives discovery a real, live target
    //  so the connection's own background thread is genuinely in flight --
    //  blocked reading the Hello handshake it will never receive -- at the
    //  moment AdapterRuntime is destroyed. That is the exact window a fixed
    //  destructor must close: without it, the connection's worker thread
    //  reports its stopped attempt through onAttemptFinished, which calls
    //  into supervisor_ regardless of whether it has already been destroyed.
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    LoopbackListener listener;
    std::filesystem::path rendezvousPath = scratchDirectory / "live-rendezvous.dat";
    WriteRendezvousFile(rendezvousPath, listener.Port());

    AdapterStartupContext startupContext{
        .instanceId = {.value = {std::byte{9}}},
        .ownerLifetimeId = {std::byte{4}, std::byte{5}, std::byte{6}},
        .rendezvousPath = rendezvousPath,
        .hostExecutablePath = scratchDirectory / "no-such-host.exe",
    };

    auto runtime = std::make_unique<AdapterRuntime>(
        startupContext, taskMarshaller, pairingSink, MakeStubCaptureRouter,
        [](const AdapterCaptureWorkItem&) {},
        [](const AdapterCaptureWorkItem&) {}, [] {});
    runtime->Start();

    listener.AcceptOne();

    //  A crash or hang here is this test's failure signal.
    runtime.reset();
}

TEST_CASE("AdapterRuntime resets its connection when the capture queue "
          "rejects a reliable Event") {
    //  Stopping the queue (rather than racing its own worker thread's drain
    //  rate to fill it to capacity) makes every subsequent TryEnqueue
    //  deterministically rejected, so this test can trigger the rejection
    //  path without depending on timing. The captureRouterFactory parameter
    //  is the only seam this test needs: it already receives a reference to
    //  the real queue AdapterRuntime owns.
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    LoopbackListener listener;
    std::filesystem::path rendezvousPath =
        scratchDirectory / "event-reject-rendezvous.dat";
    WriteRendezvousFile(rendezvousPath, listener.Port());

    AdapterStartupContext startupContext{
        .instanceId = {.value = {std::byte{11}}},
        .ownerLifetimeId = {std::byte{7}, std::byte{8}, std::byte{9}},
        .rendezvousPath = rendezvousPath,
        .hostExecutablePath = scratchDirectory / "no-such-host.exe",
    };

    IAdapterCaptureHandoffQueue* queue = nullptr;
    auto runtime = std::make_unique<AdapterRuntime>(
        startupContext, taskMarshaller, pairingSink,
        [&queue](IAdapterCaptureHandoffQueue& capturedQueue,
                 IAdapterPlayContextState&) {
            queue = &capturedQueue;
            return std::make_unique<AdapterNativeCaptureRouter>();
        },
        [](const AdapterCaptureWorkItem&) {},
        [](const AdapterCaptureWorkItem&) {}, [] {});
    runtime->Start();
    listener.AcceptOne();
    listener.DrainAvailableBytes();
    REQUIRE(queue != nullptr);

    queue->Stop();
    bool enqueued = queue->TryEnqueue(AdapterCaptureWorkItem{
        .intentKey = 1,
        .source = CaptureSourceKind::kEvent,
        .availability = CaptureAvailability::kAvailable,
    });
    REQUIRE_FALSE(enqueued);

    //  The rejected reliable Event resets the connection: the peer socket
    //  this test's own loopback listener accepted observes the connection
    //  close (a readable poll followed by a zero-byte recv).
    WSAPOLLFD pollFd{.fd = listener.AcceptedSocket(), .events = POLLRDNORM};
    int pollResult = WSAPoll(&pollFd, 1, 5000);
    REQUIRE(pollResult > 0);
    char buffer[1];
    int received = recv(listener.AcceptedSocket(), buffer, sizeof(buffer), 0);
    CHECK(received == 0);

    runtime.reset();
}

TEST_CASE("AdapterRuntime does not reset its connection when the capture "
          "queue rejects a Snapshot sample") {
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    LoopbackListener listener;
    std::filesystem::path rendezvousPath =
        scratchDirectory / "sample-reject-rendezvous.dat";
    WriteRendezvousFile(rendezvousPath, listener.Port());

    AdapterStartupContext startupContext{
        .instanceId = {.value = {std::byte{12}}},
        .ownerLifetimeId = {std::byte{10}, std::byte{11}, std::byte{12}},
        .rendezvousPath = rendezvousPath,
        .hostExecutablePath = scratchDirectory / "no-such-host.exe",
    };

    IAdapterCaptureHandoffQueue* queue = nullptr;
    auto runtime = std::make_unique<AdapterRuntime>(
        startupContext, taskMarshaller, pairingSink,
        [&queue](IAdapterCaptureHandoffQueue& capturedQueue,
                 IAdapterPlayContextState&) {
            queue = &capturedQueue;
            return std::make_unique<AdapterNativeCaptureRouter>();
        },
        [](const AdapterCaptureWorkItem&) {},
        [](const AdapterCaptureWorkItem&) {}, [] {});
    runtime->Start();
    listener.AcceptOne();
    listener.DrainAvailableBytes();
    REQUIRE(queue != nullptr);

    queue->Stop();
    bool enqueued = queue->TryEnqueue(AdapterCaptureWorkItem{
        .intentKey = 1,
        .source = CaptureSourceKind::kSample,
        .availability = CaptureAvailability::kAvailable,
    });
    REQUIRE_FALSE(enqueued);

    //  A rejected Snapshot sample is diagnostic-only, recoverable by the next
    //  poll: the connection must stay open. A bounded poll with nothing ever
    //  becoming readable (never POLLRDNORM, never POLLHUP) is this negative
    //  result's own proof within the time this test can afford to wait.
    WSAPOLLFD pollFd{.fd = listener.AcceptedSocket(), .events = POLLRDNORM};
    int pollResult = WSAPoll(&pollFd, 1, 300);
    CHECK(pollResult == 0);

    runtime.reset();
}

TEST_CASE("AdapterRuntime still resets the connection when the caller's own "
          "onCaptureQueueRejected diagnostic callback throws") {
    //  The reset is dispatched before the diagnostic callback runs, and the
    //  capture queue's own TryEnqueue already contains any exception the
    //  diagnostic callback raises (see adapter_capture_handoff_queue.cpp) --
    //  but this proves the reset itself is unconditional, not merely that
    //  the exception does not crash the process.
    FakeAdapterTaskMarshaller taskMarshaller;
    RecordingPairingNotificationSink pairingSink;
    std::filesystem::path scratchDirectory =
        std::filesystem::temp_directory_path() / "dovahlink_adapter_runtime_test";
    std::filesystem::create_directories(scratchDirectory);

    LoopbackListener listener;
    std::filesystem::path rendezvousPath =
        scratchDirectory / "event-reject-throwing-diagnostic-rendezvous.dat";
    WriteRendezvousFile(rendezvousPath, listener.Port());

    AdapterStartupContext startupContext{
        .instanceId = {.value = {std::byte{13}}},
        .ownerLifetimeId = {std::byte{13}, std::byte{14}, std::byte{15}},
        .rendezvousPath = rendezvousPath,
        .hostExecutablePath = scratchDirectory / "no-such-host.exe",
    };

    IAdapterCaptureHandoffQueue* queue = nullptr;
    auto runtime = std::make_unique<AdapterRuntime>(
        startupContext, taskMarshaller, pairingSink,
        [&queue](IAdapterCaptureHandoffQueue& capturedQueue,
                 IAdapterPlayContextState&) {
            queue = &capturedQueue;
            return std::make_unique<AdapterNativeCaptureRouter>();
        },
        [](const AdapterCaptureWorkItem&) {},
        [](const AdapterCaptureWorkItem&) -> void {
            throw std::runtime_error("simulated diagnostic-callback failure");
        },
        [] {});
    runtime->Start();
    listener.AcceptOne();
    listener.DrainAvailableBytes();
    REQUIRE(queue != nullptr);

    queue->Stop();
    bool enqueued = queue->TryEnqueue(AdapterCaptureWorkItem{
        .intentKey = 1,
        .source = CaptureSourceKind::kEvent,
        .availability = CaptureAvailability::kAvailable,
    });
    REQUIRE_FALSE(enqueued);

    WSAPOLLFD pollFd{.fd = listener.AcceptedSocket(), .events = POLLRDNORM};
    int pollResult = WSAPoll(&pollFd, 1, 5000);
    REQUIRE(pollResult > 0);
    char buffer[1];
    int received = recv(listener.AcceptedSocket(), buffer, sizeof(buffer), 0);
    CHECK(received == 0);

    runtime.reset();
}
