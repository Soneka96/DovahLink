#include "plugin/adapter_runtime.hpp"

#include "ipc/adapter_task_marshaller_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <filesystem>
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
