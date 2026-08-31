#include "process/adapter_host_shutdown_requester.hpp"

#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>

using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;
using dovahlink::adapter::process::BuildShutdownEventName;
using dovahlink::adapter::process::WindowsEventAdapterHostShutdownRequester;

namespace {

///  A representative, fixed owner-lifetime-id for tests that don't care
///  about its value.
std::array<std::byte, kIpcOwnerLifetimeIdBytes> SampleLifetimeId() {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

///  Owns a named manual-reset event for the test's lifetime, mirroring how
///  a real host creates its own shutdown event.
class ScopedTestEvent {
public:
  explicit ScopedTestEvent(const std::wstring &name)
      : handle_(CreateEventW(nullptr, /*bManualReset=*/TRUE,
                             /*bInitialState=*/FALSE, name.c_str())) {}

  ~ScopedTestEvent() {
    if (handle_ != nullptr) {
      CloseHandle(handle_);
    }
  }

  ScopedTestEvent(const ScopedTestEvent &) = delete;
  ScopedTestEvent &operator=(const ScopedTestEvent &) = delete;

  ///  Whether the event is currently signaled, checked without blocking.
  bool IsSignaled() const {
    return WaitForSingleObject(handle_, 0) == WAIT_OBJECT_0;
  }

private:
  HANDLE handle_;
};

} //  namespace

TEST_CASE("WindowsEventAdapterHostShutdownRequester::RequestShutdown is a "
          "no-op, without throwing, when no host is listening") {
  WindowsEventAdapterHostShutdownRequester requester(SampleLifetimeId());

  REQUIRE_NOTHROW(requester.RequestShutdown());
}

TEST_CASE("WindowsEventAdapterHostShutdownRequester::RequestShutdown "
          "signals a listening host's shutdown event") {
  auto lifetimeId = SampleLifetimeId();
  ScopedTestEvent hostEvent(BuildShutdownEventName(lifetimeId));
  REQUIRE_FALSE(hostEvent.IsSignaled());

  WindowsEventAdapterHostShutdownRequester requester(lifetimeId);
  requester.RequestShutdown();

  CHECK(hostEvent.IsSignaled());
}

TEST_CASE("WindowsEventAdapterHostShutdownRequester::RequestShutdown is "
          "idempotent when called more than once against a listening host") {
  auto lifetimeId = SampleLifetimeId();
  ScopedTestEvent hostEvent(BuildShutdownEventName(lifetimeId));
  WindowsEventAdapterHostShutdownRequester requester(lifetimeId);

  requester.RequestShutdown();
  REQUIRE_NOTHROW(requester.RequestShutdown());

  CHECK(hostEvent.IsSignaled());
}

TEST_CASE("BuildShutdownEventName produces different names for different "
          "owner-lifetime-ids") {
  auto first = SampleLifetimeId();
  auto second = SampleLifetimeId();
  second[0] = std::byte{0xFF};

  CHECK(BuildShutdownEventName(first) != BuildShutdownEventName(second));
}

TEST_CASE("BuildShutdownEventName is stable for the same owner-lifetime-id") {
  auto lifetimeId = SampleLifetimeId();

  CHECK(BuildShutdownEventName(lifetimeId) ==
        BuildShutdownEventName(lifetimeId));
}
