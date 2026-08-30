#include "process/adapter_host_process_launcher.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <optional>
#include <string>
#include <vector>

using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::Win32AdapterHostProcessLauncher;

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

} //  namespace

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
