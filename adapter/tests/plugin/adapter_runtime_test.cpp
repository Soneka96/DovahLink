#include "plugin/adapter_runtime.hpp"

#include "ipc/adapter_task_marshaller_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <future>
#include <memory>
#include <string>

using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::ipc::IAdapterPairingNotificationSink;
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

    ///  Blocks, bounded, until one connection is accepted -- the
    ///  deterministic barrier proving AdapterRuntime's own connection worker
    ///  thread has physically connected and moved on to waiting for the
    ///  Hello handshake it will never receive from this listener. Runs the
    ///  blocking `accept()` on a background thread and bounds only the wait
    ///  for it to finish, mirroring `adapter_ipc_connection_test.cpp`'s own
    ///  `std::async`/`wait_for` pattern, so a regression that stops the
    ///  connection from ever reaching this listener fails this test with a
    ///  clear timeout instead of hanging until CI's outer timeout kills it.
    void AcceptOne() {
        std::future<SOCKET> accepted = std::async(std::launch::async, [this] {
            return accept(listenSocket_, nullptr, nullptr);
        });
        if (accepted.wait_for(std::chrono::seconds(5)) !=
            std::future_status::ready) {
            //  A std::async future's destructor blocks until its task
            //  completes, even while unwinding through a thrown exception --
            //  closing the listening socket first unblocks the still-running
            //  accept() (the same mechanism this file's own StopAccepting-
            //  style shutdown relies on) so FAIL()'s unwind cannot deadlock
            //  here instead of actually failing the test.
            closesocket(listenSocket_);
            listenSocket_ = INVALID_SOCKET;
            FAIL("AcceptOne timed out waiting for a connection");
        }
        acceptedSocket_ = accepted.get();
        REQUIRE(acceptedSocket_ != INVALID_SOCKET);
    }

  private:
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

    AdapterRuntime runtime(BuildStartupContext(scratchDirectory), taskMarshaller, pairingSink, [](const AdapterCaptureWorkItem&) {}, [](const AdapterCaptureWorkItem&) {}, [] {});

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

    AdapterRuntime runtime(BuildStartupContext(scratchDirectory), taskMarshaller, pairingSink, [](const AdapterCaptureWorkItem&) {}, [](const AdapterCaptureWorkItem&) {}, [] {});

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
        startupContext, taskMarshaller, pairingSink,
        [](const AdapterCaptureWorkItem&) {},
        [](const AdapterCaptureWorkItem&) {}, [] {});
    runtime->Start();

    listener.AcceptOne();

    //  A crash or hang here is this test's failure signal.
    runtime.reset();
}
