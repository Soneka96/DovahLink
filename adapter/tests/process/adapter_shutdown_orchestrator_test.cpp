#include "process/adapter_shutdown_orchestrator.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_host_supervisor.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::AdapterShutdownOrchestrator;
using dovahlink::adapter::process::IAdapterHostProcessLauncher;
using dovahlink::adapter::process::IAdapterHostShutdownRequester;
using dovahlink::adapter::process::IAdapterHostSupervisor;

namespace {

///  Records the order in which the orchestrator's collaborators are called,
///  so a test can assert the documented five-step sequence directly.
class CallLog {
public:
  void Record(std::string call) {
    std::lock_guard<std::mutex> lock(mutex_);
    calls_.push_back(std::move(call));
  }

  std::vector<std::string> Calls() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return calls_;
  }

private:
  mutable std::mutex mutex_;
  std::vector<std::string> calls_;
};

class FakeSupervisor final : public IAdapterHostSupervisor {
public:
  explicit FakeSupervisor(CallLog &log) : log_(log) {}

  ///  Makes `RequestStop` throw instead of recording its call.
  void SetThrows() { throws_ = true; }

  void Start() override {}

  void RequestStop() override {
    if (throws_) {
      throw std::runtime_error("RequestStop failed unexpectedly");
    }
    log_.Record("supervisor.RequestStop");
  }

  void NotifyConnectionLost() override {}

private:
  CallLog &log_;
  bool throws_ = false;
};

class FakeShutdownRequester final : public IAdapterHostShutdownRequester {
public:
  explicit FakeShutdownRequester(CallLog &log) : log_(log) {}

  void RequestShutdown() override {
    log_.Record("shutdownRequester.RequestShutdown");
  }

private:
  CallLog &log_;
};

///  A deterministic `IAdapterHostProcessLauncher` test double that records
///  every call and the timeout `AwaitExitOrTerminate` was given.
class FakeLauncher final : public IAdapterHostProcessLauncher {
public:
  explicit FakeLauncher(CallLog &log) : log_(log) {}

  std::optional<AdapterHostEndpoint> Launch() override { return std::nullopt; }

  bool AwaitExitOrTerminate(std::chrono::milliseconds timeout) override {
    log_.Record("launcher.AwaitExitOrTerminate");
    lastTimeout_ = timeout;
    return true;
  }

  void Release() override { log_.Record("launcher.Release"); }

  ///  The `timeout` most recently passed to `AwaitExitOrTerminate`.
  std::chrono::milliseconds LastTimeout() const { return lastTimeout_; }

private:
  CallLog &log_;
  std::chrono::milliseconds lastTimeout_{0};
};

class FakeConnection final
    : public dovahlink::adapter::ipc::IAdapterIpcConnection {
public:
  explicit FakeConnection(CallLog &log) : log_(log) {}

  void Start() override {}
  bool TrySend(const dovahlink::adapter::ipc::IpcMessage &) override {
    return true;
  }
  void Stop() override { log_.Record("connection.Stop"); }

private:
  CallLog &log_;
};

} //  namespace

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown performs the "
          "five documented steps in order") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  orchestrator.RunOrderedShutdown();

  CHECK(log.Calls() ==
        std::vector<std::string>{"supervisor.RequestStop",
                                 "shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown passes the "
          "configured graceful-shutdown bound to AwaitExitOrTerminate") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  std::chrono::milliseconds configuredBound(1234);
  AdapterShutdownOrchestrator orchestrator(
      supervisor, shutdownRequester, launcher, connection, configuredBound);

  orchestrator.RunOrderedShutdown();

  CHECK(launcher.LastTimeout() == configuredBound);
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown still releases "
          "the launched host's handle, then rethrows, when an earlier step "
          "throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  supervisor.SetThrows();
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::runtime_error);

  //  Release must still have run despite the earlier failure, and nothing
  //  after the throwing step (the signal, the wait, stopping the
  //  connection) may have run at all.
  CHECK(log.Calls() == std::vector<std::string>{"launcher.Release"});
}
