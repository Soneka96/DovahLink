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

  ///  Makes `RequestStop` throw a non-standard exception.
  void SetThrowsNonStandard() { throwsNonStandard_ = true; }

  void Start() override {}

  void RequestStop() override {
    if (throwsNonStandard_) {
      throw 42;
    }
    if (throws_) {
      throw std::logic_error("RequestStop failed unexpectedly");
    }
    log_.Record("supervisor.RequestStop");
  }

  void NotifyConnectionLost() override {}

private:
  CallLog &log_;
  bool throws_ = false;
  bool throwsNonStandard_ = false;
};

class FakeShutdownRequester final : public IAdapterHostShutdownRequester {
public:
  explicit FakeShutdownRequester(CallLog &log) : log_(log) {}

  ///  Makes `RequestShutdown` throw after recording its call.
  void SetThrows() { throws_ = true; }

  void RequestShutdown() override {
    log_.Record("shutdownRequester.RequestShutdown");
    if (throws_) {
      throw std::runtime_error("RequestShutdown failed unexpectedly");
    }
  }

private:
  CallLog &log_;
  bool throws_ = false;
};

///  A deterministic `IAdapterHostProcessLauncher` test double that records
///  every call and the timeout `AwaitExitOrTerminate` was given.
class FakeLauncher final : public IAdapterHostProcessLauncher {
public:
  explicit FakeLauncher(CallLog &log) : log_(log) {}

  ///  Makes `AwaitExitOrTerminate` throw after recording its call.
  void SetAwaitThrows() { awaitThrows_ = true; }

  ///  Makes `AwaitExitOrTerminate` report that force-termination was needed.
  void SetAwaitRequiresForceTermination() { awaitResult_ = false; }

  ///  Makes `Release` throw after recording its call.
  void SetReleaseThrows() { releaseThrows_ = true; }

  std::optional<AdapterHostEndpoint> Launch(std::stop_token) override {
    return std::nullopt;
  }

  bool AwaitExitOrTerminate(std::chrono::milliseconds timeout) override {
    log_.Record("launcher.AwaitExitOrTerminate");
    lastTimeout_ = timeout;
    if (awaitThrows_) {
      throw std::runtime_error("AwaitExitOrTerminate failed unexpectedly");
    }
    return awaitResult_;
  }

  void Release() override {
    log_.Record("launcher.Release");
    if (releaseThrows_) {
      throw std::runtime_error("Release failed unexpectedly");
    }
  }

  ///  The `timeout` most recently passed to `AwaitExitOrTerminate`.
  std::chrono::milliseconds LastTimeout() const { return lastTimeout_; }

private:
  CallLog &log_;
  std::chrono::milliseconds lastTimeout_{0};
  bool awaitThrows_ = false;
  bool awaitResult_ = true;
  bool releaseThrows_ = false;
};

class FakeConnection final
    : public dovahlink::adapter::ipc::IAdapterIpcConnection {
public:
  explicit FakeConnection(CallLog &log) : log_(log) {}

  ///  Makes `Stop` throw after recording its call.
  void SetThrows() { throws_ = true; }

  void Start() override {}
  bool TrySend(const dovahlink::adapter::ipc::IpcMessage &) override {
    return true;
  }
  void Stop() override {
    log_.Record("connection.Stop");
    if (throws_) {
      throw std::runtime_error("Stop failed unexpectedly");
    }
  }

private:
  CallLog &log_;
  bool throws_ = false;
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

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown continues ordered "
          "cleanup and rethrows when supervisor stop throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  supervisor.SetThrows();
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::logic_error);

  CHECK(log.Calls() ==
        std::vector<std::string>{"shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown continues ordered "
          "cleanup and rethrows when shutdown signaling throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  shutdownRequester.SetThrows();
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::runtime_error);

  CHECK(log.Calls() ==
        std::vector<std::string>{"supervisor.RequestStop",
                                 "shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown continues ordered "
          "cleanup and rethrows when the bounded host wait throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  launcher.SetAwaitThrows();
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::runtime_error);

  CHECK(log.Calls() ==
        std::vector<std::string>{"supervisor.RequestStop",
                                 "shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown releases last and "
          "rethrows when connection stop throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  connection.SetThrows();
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::runtime_error);

  CHECK(log.Calls() ==
        std::vector<std::string>{"supervisor.RequestStop",
                                 "shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown rethrows the first "
          "exception while continuing later phases") {
  CallLog log;
  FakeSupervisor supervisor(log);
  supervisor.SetThrows();
  FakeShutdownRequester shutdownRequester(log);
  shutdownRequester.SetThrows();
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::logic_error);
  CHECK(log.Calls() ==
        std::vector<std::string>{"shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown preserves a non-"
          "standard exception while continuing cleanup") {
  CallLog log;
  FakeSupervisor supervisor(log);
  supervisor.SetThrowsNonStandard();
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS(orchestrator.RunOrderedShutdown());
  CHECK(log.Calls() ==
        std::vector<std::string>{"shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown releases last and "
          "rethrows when final release throws") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  launcher.SetReleaseThrows();
  FakeConnection connection(log);
  AdapterShutdownOrchestrator orchestrator(supervisor, shutdownRequester,
                                           launcher, connection);

  CHECK_THROWS_AS(orchestrator.RunOrderedShutdown(), std::runtime_error);
  CHECK(log.Calls() ==
        std::vector<std::string>{"supervisor.RequestStop",
                                 "shutdownRequester.RequestShutdown",
                                 "launcher.AwaitExitOrTerminate",
                                 "connection.Stop", "launcher.Release"});
}

TEST_CASE("AdapterShutdownOrchestrator::RunOrderedShutdown preserves ordering "
          "when the host requires force-termination") {
  CallLog log;
  FakeSupervisor supervisor(log);
  FakeShutdownRequester shutdownRequester(log);
  FakeLauncher launcher(log);
  launcher.SetAwaitRequiresForceTermination();
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
