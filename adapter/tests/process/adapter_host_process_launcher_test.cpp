#include "process/adapter_host_process_launcher.hpp"
#include "test_support/source_text_test_support.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <future>
#include <optional>
#include <stop_token>
#include <string>
#include <thread>
#include <vector>

using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::Win32AdapterHostProcessLauncher;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  A representative, fixed owner-lifetime-id for tests that don't care
///  about its value.
std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
SampleLifetimeId() {
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

///  The path to `dovahlink_adapter_launcher_test_fixture`, this test file's
///  own small, controllable stand-in host process (see
///  adapter_host_launcher_test_fixture_process.cpp).
std::filesystem::path FixtureExecutablePath() {
  return std::filesystem::path{DOVAHLINK_ADAPTER_LAUNCHER_TEST_FIXTURE_EXE};
}

///  Sets an environment variable for this test's lifetime, restoring it to
///  unset afterward, so the fixture process's env-var-controlled behavior
///  (see adapter_host_launcher_test_fixture_process.cpp) never leaks into a
///  later test run in the same process.
class ScopedEnvironmentVariable {
public:
  ScopedEnvironmentVariable(const wchar_t *name, const wchar_t *value)
      : name_(name) {
    SetEnvironmentVariableW(name_, value);
  }

  ~ScopedEnvironmentVariable() { SetEnvironmentVariableW(name_, nullptr); }

  ScopedEnvironmentVariable(const ScopedEnvironmentVariable &) = delete;
  ScopedEnvironmentVariable &
  operator=(const ScopedEnvironmentVariable &) = delete;

private:
  ///  The environment variable name this instance owns, restored to unset
  ///  on destruction.
  const wchar_t *name_;
};

///  Owns one inheritable event handle used to detect accidental child-handle
///  inheritance by the launcher.
class ScopedInheritableEvent {
public:
  ScopedInheritableEvent() {
    SECURITY_ATTRIBUTES attributes{};
    attributes.nLength = sizeof(attributes);
    attributes.bInheritHandle = TRUE;
    handle_ = CreateEventW(&attributes, FALSE, FALSE, nullptr);
  }

  ~ScopedInheritableEvent() {
    if (handle_ != nullptr) {
      CloseHandle(handle_);
    }
  }

  ScopedInheritableEvent(const ScopedInheritableEvent &) = delete;
  ScopedInheritableEvent &operator=(const ScopedInheritableEvent &) = delete;

  HANDLE Get() const { return handle_; }

private:
  HANDLE handle_ = nullptr;
};

} //  namespace

TEST_CASE("Win32AdapterHostProcessLauncher associates the child with its Job "
          "Object at process creation",
          "[process][structural]") {
  std::filesystem::path sourcePath =
      std::filesystem::path(DOVAHLINK_ADAPTER_PROCESS_DIR) /
      "adapter_host_process_launcher.cpp";
  std::string source = ReadSource(sourcePath);

  std::size_t createProcess = source.find("CreateProcessW(");
  REQUIRE(createProcess != std::string::npos);

  CHECK(source.find("STARTUPINFOEXW") != std::string::npos);
  CHECK(source.find("CreateJobObjectW") < createProcess);
  CHECK(source.find("SetInformationJobObject") < createProcess);
  CHECK(source.find("PROC_THREAD_ATTRIBUTE_JOB_LIST") < createProcess);
  CHECK(source.find("PROC_THREAD_ATTRIBUTE_HANDLE_LIST") < createProcess);
  CHECK(source.find("UpdateProcThreadAttribute") < createProcess);
  CHECK(source.find("EXTENDED_STARTUPINFO_PRESENT", createProcess) !=
        std::string::npos);
  CHECK(source.find("startupInfoEx.lpAttributeList = attributeList") !=
        std::string::npos);
  CHECK(source.find("&startupInfoEx.StartupInfo", createProcess) !=
        std::string::npos);
  CHECK(source.find("bInheritHandles=*/TRUE") != std::string::npos);
  CHECK(source.find("DeleteProcThreadAttributeList") != std::string::npos);
  CHECK(source.find("CREATE_SUSPENDED") == std::string::npos);
  CHECK(source.find("AssignProcessToJobObject") == std::string::npos);
  CHECK(source.find("ScopedHandle") != std::string::npos);
  CHECK(source.find("ScopedProcThreadAttributeList") != std::string::npos);
}

TEST_CASE("Win32AdapterHostProcessLauncher inherits only the rendezvous pipe",
          "[process][integration]") {
  ScopedInheritableEvent unrelatedHandle;
  REQUIRE(unrelatedHandle.Get() != nullptr);
  std::wstring handleValue =
      std::to_wstring(reinterpret_cast<std::uintptr_t>(unrelatedHandle.Get()));
  ScopedEnvironmentVariable probe(L"DOVAHLINK_TEST_HOST_UNRELATED_HANDLE",
                                  handleValue.c_str());

  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  std::optional<AdapterHostEndpoint> endpoint = launcher.Launch();

  REQUIRE(endpoint.has_value());
  CHECK(endpoint->port == 4242);
  //  The fixture reports PROOF ab only when GetHandleInformation cannot open
  //  the unrelated handle value in the child process.
  CHECK(endpoint->proofToken == std::vector<std::byte>{std::byte{0xAB}});
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch starts the packaged host "
          "hidden and returns the endpoint it reported") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());

  std::optional<AdapterHostEndpoint> endpoint = launcher.Launch();

  REQUIRE(endpoint.has_value());
  CHECK(endpoint->port == 4242);
  CHECK(endpoint->proofToken == std::vector<std::byte>{std::byte{0xAB}});

  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::AwaitExitOrTerminate returns "
          "true, without force-terminating, when the process exits within "
          "the bound") {
  ScopedEnvironmentVariable sleepOverride(L"DOVAHLINK_TEST_HOST_SLEEP_MS",
                                          L"50");
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());

  CHECK(launcher.AwaitExitOrTerminate(std::chrono::seconds(5)));
}

TEST_CASE("Win32AdapterHostProcessLauncher::AwaitExitOrTerminate "
          "force-terminates and returns false when the process exceeds the "
          "bound") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());

  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200)));
  //  The process is now dead (force-terminated): a further wait on the same
  //  retained handle must return true immediately, proving it actually
  //  exited rather than merely timing out again.
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}

TEST_CASE("Win32AdapterHostProcessLauncher::AwaitExitOrTerminate is a no-op "
          "that returns true when nothing has been launched") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());

  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}

TEST_CASE("Win32AdapterHostProcessLauncher reports and clears the launched "
          "host process id") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());

  CHECK(launcher.ProcessId() == 0);
  REQUIRE(launcher.Launch().has_value());
  CHECK(launcher.ProcessId() != 0);

  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0));
  launcher.Release();
  CHECK(launcher.ProcessId() == 0);
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch returns nullopt when the "
          "process never reports its endpoint within the bound") {
  ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId(),
                                           std::chrono::milliseconds(200));

  CHECK_FALSE(launcher.Launch().has_value());
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch does not create a child "
          "when already cancelled") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  std::stop_source cancellation;
  cancellation.request_stop();

  CHECK_FALSE(launcher.Launch(cancellation.get_token()).has_value());
  CHECK(launcher.ProcessId() == 0);
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch cancels an in-flight "
          "endpoint wait and cleans up the child") {
  ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
  Win32AdapterHostProcessLauncher launcher(
      FixtureExecutablePath(), SampleLifetimeId(), std::chrono::seconds(5));
  std::stop_source cancellation;

  std::future<std::optional<AdapterHostEndpoint>> result =
      std::async(std::launch::async,
                 [&] { return launcher.Launch(cancellation.get_token()); });
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  cancellation.request_stop();

  REQUIRE(result.wait_for(std::chrono::seconds(2)) ==
          std::future_status::ready);
  CHECK_FALSE(result.get().has_value());
  CHECK(launcher.ProcessId() == 0);
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch returns nullopt when the "
          "executable does not exist") {
  Win32AdapterHostProcessLauncher launcher(
      "C:/DovahLink-test-nonexistent-executable.exe", SampleLifetimeId());

  CHECK_FALSE(launcher.Launch().has_value());
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch returns nullopt promptly "
          "when the process exits without ever reporting anything") {
  ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
  ScopedEnvironmentVariable exitImmediately(L"DOVAHLINK_TEST_HOST_SLEEP_MS",
                                            L"0");
  //  A generous bound: this proves the pipe breaking (the process exiting)
  //  is detected on its own, distinct from the deadline-elapsed path the
  //  "never reports... within the bound" test above already covers.
  Win32AdapterHostProcessLauncher launcher(
      FixtureExecutablePath(), SampleLifetimeId(), std::chrono::seconds(5));

  CHECK_FALSE(launcher.Launch().has_value());
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch returns nullopt when the "
          "process writes past the bounded report buffer without ever "
          "completing two lines") {
  ScopedEnvironmentVariable spam(L"DOVAHLINK_TEST_HOST_SPAM", L"1");
  Win32AdapterHostProcessLauncher launcher(
      FixtureExecutablePath(), SampleLifetimeId(), std::chrono::seconds(5));

  CHECK_FALSE(launcher.Launch().has_value());
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch succeeds on a later call "
          "on the same instance after an earlier call failed") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId(),
                                           std::chrono::milliseconds(200));
  {
    ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
    REQUIRE_FALSE(launcher.Launch().has_value());
  }

  //  The same instance, same target: the failed first attempt's own cleanup
  //  must not have left any state that breaks a later successful call.
  std::optional<AdapterHostEndpoint> second = launcher.Launch();

  REQUIRE(second.has_value());
  CHECK(second->port == 4242);
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch releases a previously "
          "launched process before starting a new one") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());

  std::optional<AdapterHostEndpoint> second = launcher.Launch();

  REQUIRE(second.has_value());
  CHECK(second->port == 4242);
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::Release clears any held handle "
          "so a later AwaitExitOrTerminate becomes a no-op") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());

  launcher.Release();
  //  A second, redundant call: idempotent, since nothing is held anymore.
  REQUIRE_NOTHROW(launcher.Release());

  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}

TEST_CASE("Win32AdapterHostProcessLauncher's destructor releases a still-"
          "running launched process without hanging") {
  {
    Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                             SampleLifetimeId());
    REQUIRE(launcher.Launch().has_value());
    //  Deliberately no AwaitExitOrTerminate/Release call: the destructor
    //  alone must clean up a process that is still running (the fixture's
    //  default sleep is long) when this scope ends.
  }
  SUCCEED("destructor returned without hanging");
}

TEST_CASE("Win32AdapterHostProcessLauncher::Release is a no-op when nothing "
          "has been launched") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());

  REQUIRE_NOTHROW(launcher.Release());
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch does not destroy the "
          "retained host when a replacement attempt is already cancelled") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());
  std::uint32_t retainedProcessId = launcher.ProcessId();
  REQUIRE(retainedProcessId != 0);
  //  Opened independently of the launcher so this test can prove liveness
  //  without going through AwaitExitOrTerminate, which would force-terminate
  //  a still-running process merely by checking it.
  HANDLE retainedProcessHandle =
      OpenProcess(SYNCHRONIZE, /*bInheritHandle=*/FALSE, retainedProcessId);
  REQUIRE(retainedProcessHandle != nullptr);
  std::stop_source cancellation;
  cancellation.request_stop();

  std::optional<AdapterHostEndpoint> replacement =
      launcher.Launch(cancellation.get_token());

  CHECK_FALSE(replacement.has_value());
  CHECK(launcher.ProcessId() == retainedProcessId);
  CHECK(WaitForSingleObject(retainedProcessHandle, 0) == WAIT_TIMEOUT);
  CloseHandle(retainedProcessHandle);
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch preserves the retained "
          "host when cancellation arrives while a replacement is still "
          "being validated") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId());
  REQUIRE(launcher.Launch().has_value());
  std::uint32_t retainedProcessId = launcher.ProcessId();
  ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
  std::stop_source cancellation;

  std::future<std::optional<AdapterHostEndpoint>> replacement =
      std::async(std::launch::async,
                 [&] { return launcher.Launch(cancellation.get_token()); });
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  cancellation.request_stop();

  REQUIRE(replacement.wait_for(std::chrono::seconds(2)) ==
          std::future_status::ready);
  CHECK_FALSE(replacement.get().has_value());
  //  Checked only after the replacement attempt has fully returned: the
  //  launcher has no internal synchronization of its own and relies on its
  //  caller (normally the supervisor's operationMutex_) to serialize access,
  //  so reading ProcessId() while Launch() is still in flight would itself
  //  be a race.
  CHECK(launcher.ProcessId() == retainedProcessId);
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}

TEST_CASE("Win32AdapterHostProcessLauncher::Launch preserves the retained "
          "host when a replacement attempt fails for a reason other than "
          "cancellation") {
  Win32AdapterHostProcessLauncher launcher(FixtureExecutablePath(),
                                           SampleLifetimeId(),
                                           std::chrono::milliseconds(200));
  REQUIRE(launcher.Launch().has_value());
  std::uint32_t retainedProcessId = launcher.ProcessId();

  //  No cancellation at all here: the replacement fails only because it
  //  never reports its endpoint within the bound, proving the fix protects
  //  the retained host against any failed replacement, not merely a
  //  cancelled one.
  ScopedEnvironmentVariable silent(L"DOVAHLINK_TEST_HOST_SILENT", L"1");
  std::optional<AdapterHostEndpoint> replacement = launcher.Launch();

  CHECK_FALSE(replacement.has_value());
  CHECK(launcher.ProcessId() == retainedProcessId);
  launcher.AwaitExitOrTerminate(std::chrono::milliseconds(200));
}
