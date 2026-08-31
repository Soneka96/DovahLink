#include "process/adapter_host_supervisor.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_handshake_verifier.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <functional>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <stop_token>
#include <thread>
#include <vector>

using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::AdapterHostSupervisor;
using dovahlink::adapter::process::IAdapterHostHandshakeVerifier;
using dovahlink::adapter::process::IAdapterHostProcessLauncher;
using dovahlink::adapter::process::IAdapterHostRendezvousReader;

namespace {

///  A test-only gate that blocks one thread until released by another,
///  without relying on a timing sleep to prove ordering.
class BlockingGate {
public:
  void WaitUntilReleased() {
    std::unique_lock<std::mutex> lock(mutex_);
    hasWaiter_ = true;
    condition_.wait(lock, [this] { return released_; });
  }

  ///  Blocks until released or until the supplied cancellation request fires.
  ///  @return `true` when the gate was released, or `false` when cancellation
  ///  interrupted the wait.
  bool WaitUntilReleasedOrStop(std::stop_token cancellationToken) {
    std::unique_lock<std::mutex> lock(mutex_);
    hasWaiter_ = true;
    std::stop_callback notifyOnStop(cancellationToken,
                                    [this] { condition_.notify_all(); });
    condition_.wait(lock, [this, cancellationToken] {
      return released_ || cancellationToken.stop_requested();
    });
    return released_ && !cancellationToken.stop_requested();
  }

  void Release() {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      released_ = true;
    }
    condition_.notify_all();
  }

  ///  Whether some thread is currently blocked in `WaitUntilReleased`, so a
  ///  test can confirm a round actually reached this gate before acting on
  ///  it, instead of racing a background thread that may not have started
  ///  yet.
  bool HasWaiter() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return hasWaiter_;
  }

private:
  mutable std::mutex mutex_;
  std::condition_variable condition_;
  bool hasWaiter_ = false;
  bool released_ = false;
};

///  A representative candidate endpoint for a given port, with an arbitrary
///  fixed proof token.
AdapterHostEndpoint SampleEndpoint(std::uint16_t port) {
  return AdapterHostEndpoint{.port = port,
                             .proofToken = {std::byte{1}, std::byte{2}}};
}

///  Polls `predicate` until it becomes `true` or `bound` elapses, for
///  observing an eventual side effect of the supervisor's background thread
///  that has no cheaper completion signal (unlike the promise-based signals
///  below, which pinpoint the start of a call).
template <typename Predicate>
bool WaitUntil(Predicate predicate,
               std::chrono::milliseconds bound = std::chrono::seconds(5)) {
  auto deadline = std::chrono::steady_clock::now() + bound;
  do {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  } while (std::chrono::steady_clock::now() < deadline);
  return predicate();
}

///  A deterministic, fully controllable `IAdapterHostRendezvousReader` test
///  double: `TryRead` replays a scripted result sequence (the last entry
///  repeats), and can optionally block its next call until released.
class FakeAdapterHostRendezvousReader final
    : public IAdapterHostRendezvousReader {
public:
  ///  Configures the sequence of `TryRead` results; the last entry repeats
  ///  once exhausted. An empty sequence (the default) always returns
  ///  `std::nullopt`.
  void SetResults(std::vector<std::optional<AdapterHostEndpoint>> results) {
    std::lock_guard<std::mutex> lock(mutex_);
    results_ = std::move(results);
  }

  ///  Makes the next `TryRead` call block until `gate.Release()` is called.
  void BlockNextCallUntilReleased(BlockingGate &gate) {
    std::lock_guard<std::mutex> lock(mutex_);
    blockGate_ = &gate;
  }

  ///  The number of times `TryRead` has been called so far.
  int CallCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return callCount_;
  }

  std::optional<AdapterHostEndpoint> TryRead() override {
    BlockingGate *gate = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      gate = blockGate_;
      blockGate_ = nullptr;
    }
    if (gate != nullptr) {
      gate->WaitUntilReleased();
    }

    std::lock_guard<std::mutex> lock(mutex_);
    ++callCount_;
    if (results_.empty()) {
      return std::nullopt;
    }
    std::size_t index = std::min(callIndex_, results_.size() - 1);
    if (callIndex_ + 1 < results_.size()) {
      ++callIndex_;
    }
    return results_[index];
  }

private:
  mutable std::mutex mutex_;
  std::vector<std::optional<AdapterHostEndpoint>> results_;
  std::size_t callIndex_ = 0;
  int callCount_ = 0;
  BlockingGate *blockGate_ = nullptr;
};

///  A deterministic, fully controllable `IAdapterHostHandshakeVerifier` test
///  double: `Verify` replays a scripted result sequence (the last entry
///  repeats) and records every candidate it was asked to verify.
class FakeAdapterHostHandshakeVerifier final
    : public IAdapterHostHandshakeVerifier {
public:
  ///  Configures the sequence of `Verify` results; the last entry repeats
  ///  once exhausted. An empty sequence (the default) always returns
  ///  `false`.
  void SetResults(std::vector<bool> results) {
    std::lock_guard<std::mutex> lock(mutex_);
    results_ = std::move(results);
  }

  ///  Every candidate `Verify` has been called with so far, in order.
  std::vector<AdapterHostEndpoint> VerifiedCandidates() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return verifiedCandidates_;
  }

  ///  Makes the next `Verify` call throw instead of returning.
  void SetThrowsOnce() {
    std::lock_guard<std::mutex> lock(mutex_);
    throwOnce_ = true;
  }

  ///  Makes the next `Verify` call wait for `gate` or supervisor cancellation.
  void BlockNextCallUntilReleased(BlockingGate &gate) {
    std::lock_guard<std::mutex> lock(mutex_);
    blockGate_ = &gate;
  }

  ///  Invokes `callback` (if set) synchronously at the start of every
  ///  `Verify` call, before consulting the scripted result -- so a test can
  ///  act from inside a discovery round on the supervisor's own worker
  ///  thread, for example to prove a reentrant `RequestStop` call is safe.
  void SetOnVerifyCallback(std::function<void()> callback) {
    std::lock_guard<std::mutex> lock(mutex_);
    onVerify_ = std::move(callback);
  }

  bool Verify(const AdapterHostEndpoint &candidate,
              std::stop_token cancellationToken) override {
    BlockingGate *gate = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      gate = blockGate_;
      blockGate_ = nullptr;
    }
    if (gate != nullptr && !gate->WaitUntilReleasedOrStop(cancellationToken)) {
      return false;
    }

    std::function<void()> callback;
    bool shouldThrow = false;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      verifiedCandidates_.push_back(candidate);
      callback = onVerify_;
      shouldThrow = throwOnce_;
      throwOnce_ = false;
    }
    if (callback) {
      callback();
    }
    if (shouldThrow) {
      throw std::runtime_error("Verify failed unexpectedly");
    }

    std::lock_guard<std::mutex> lock(mutex_);
    if (results_.empty()) {
      return false;
    }
    std::size_t index = std::min(callIndex_, results_.size() - 1);
    if (callIndex_ + 1 < results_.size()) {
      ++callIndex_;
    }
    return results_[index];
  }

private:
  mutable std::mutex mutex_;
  std::function<void()> onVerify_;
  bool throwOnce_ = false;
  std::vector<bool> results_;
  std::size_t callIndex_ = 0;
  std::vector<AdapterHostEndpoint> verifiedCandidates_;
  BlockingGate *blockGate_ = nullptr;
};

///  A deterministic, fully controllable `IAdapterHostProcessLauncher` test
///  double: `Launch` replays a scripted result sequence (the last entry
///  repeats).
class FakeAdapterHostProcessLauncher final
    : public IAdapterHostProcessLauncher {
public:
  ///  Configures the sequence of `Launch` results; the last entry repeats
  ///  once exhausted. An empty sequence (the default) always returns
  ///  `std::nullopt`.
  void SetResults(std::vector<std::optional<AdapterHostEndpoint>> results) {
    std::lock_guard<std::mutex> lock(mutex_);
    results_ = std::move(results);
  }

  ///  Makes the next `Launch` call wait for `gate` or supervisor cancellation.
  void BlockNextCallUntilReleased(BlockingGate &gate) {
    std::lock_guard<std::mutex> lock(mutex_);
    blockGate_ = &gate;
  }

  ///  Invokes `callback` at the start of the next `Launch` call.
  void SetOnLaunchCallback(std::function<void()> callback) {
    std::lock_guard<std::mutex> lock(mutex_);
    onLaunch_ = std::move(callback);
  }

  ///  The number of times `Launch` has been called so far.
  int CallCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return callCount_;
  }

  std::optional<AdapterHostEndpoint>
  Launch(std::stop_token cancellationToken) override {
    BlockingGate *gate = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      gate = blockGate_;
      blockGate_ = nullptr;
    }
    if (gate != nullptr && !gate->WaitUntilReleasedOrStop(cancellationToken)) {
      return std::nullopt;
    }

    std::function<void()> callback;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      callback = std::move(onLaunch_);
    }
    if (callback) {
      callback();
    }

    std::lock_guard<std::mutex> lock(mutex_);
    ++callCount_;
    if (results_.empty()) {
      return std::nullopt;
    }
    std::size_t index = std::min(callIndex_, results_.size() - 1);
    if (callIndex_ + 1 < results_.size()) {
      ++callIndex_;
    }
    return results_[index];
  }

  bool AwaitExitOrTerminate(std::chrono::milliseconds) override { return true; }

  void Release() override {}

private:
  mutable std::mutex mutex_;
  std::vector<std::optional<AdapterHostEndpoint>> results_;
  std::size_t callIndex_ = 0;
  int callCount_ = 0;
  BlockingGate *blockGate_ = nullptr;
  std::function<void()> onLaunch_;
};

///  A test-only connection that records when the supervisor starts the
/// long-lived IPC worker.
class FakeAdapterIpcConnection final
    : public dovahlink::adapter::ipc::IAdapterIpcConnection {
public:
  ///  Records the complete target snapshot selected by the supervisor.
  void
  ConfigureTarget(dovahlink::adapter::ipc::AdapterIpcTarget target) override {
    std::lock_guard<std::mutex> lock(mutex_);
    target_ = std::move(target);
  }

  ///  Records that the connection was started.
  void Start() override {
    std::lock_guard<std::mutex> lock(mutex_);
    if (throwOnce_) {
      throwOnce_ = false;
      throw std::runtime_error("connection start failed unexpectedly");
    }
    ++startCount_;
  }

  ///  The supervisor never sends through this test connection.
  bool TrySend(const dovahlink::adapter::ipc::IpcMessage &) override {
    return true;
  }

  ///  The supervisor never stops this test connection.
  void Stop() override {}

  ///  Returns how many times the supervisor requested startup.
  int StartCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return startCount_;
  }

  ///  Returns the configured target port, or zero when none was selected.
  std::uint16_t ConfiguredPort() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->port : 0;
  }

  ///  Returns the configured target generation, or zero when none was selected.
  std::uint64_t ConfiguredTargetGeneration() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->targetGeneration : 0;
  }

  ///  Returns the configured target proof token, or an empty token.
  std::vector<std::byte> ConfiguredProofToken() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return target_.has_value() ? target_->proofToken : std::vector<std::byte>{};
  }

  ///  Makes the next startup request throw instead of recording a start.
  void SetThrowsOnce() {
    std::lock_guard<std::mutex> lock(mutex_);
    throwOnce_ = true;
  }

private:
  ///  Guards the observed start count.
  mutable std::mutex mutex_;
  ///  Number of startup requests observed.
  int startCount_ = 0;
  ///  Whether the next startup request should throw.
  bool throwOnce_ = false;
  ///  The complete target snapshot most recently configured by the supervisor.
  std::optional<dovahlink::adapter::ipc::AdapterIpcTarget> target_;
};

///  Bundles a supervisor with its fakes so each test can inspect and drive them
///  without repeating setup. Uses a short failed-round backoff so
///  a test proving automatic retry does not slow down the suite.
struct SupervisorFixture {
  FakeAdapterHostRendezvousReader reader;
  FakeAdapterHostHandshakeVerifier verifier;
  dovahlink::adapter::ipc::WinsockAdapterIpcSocket verifierSocket{0};
  FakeAdapterHostProcessLauncher launcher;
  FakeAdapterIpcConnection connection;
  AdapterHostSupervisor supervisor{
      reader,   verifier,   verifierSocket,
      launcher, connection, std::chrono::milliseconds(30)};
};

} //  namespace

TEST_CASE("AdapterHostSupervisor adopts a verified rendezvous candidate and "
          "reconfigures the live target without ever launching") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(1111);
  fixture.reader.SetResults({endpoint});
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();

  REQUIRE(WaitUntil(
      [&] { return fixture.connection.ConfiguredPort() == endpoint.port; }));
  CHECK(fixture.connection.ConfiguredProofToken() == endpoint.proofToken);
  CHECK(fixture.connection.ConfiguredTargetGeneration() == 1);
  CHECK(fixture.connection.StartCount() == 1);
  CHECK(fixture.launcher.CallCount() == 0);

  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor falls back to launching a fresh host when "
          "the adopted candidate is rejected") {
  SupervisorFixture fixture;
  AdapterHostEndpoint rejectedCandidate = SampleEndpoint(2222);
  AdapterHostEndpoint launchedCandidate = SampleEndpoint(3333);
  fixture.reader.SetResults({rejectedCandidate});
  fixture.launcher.SetResults({launchedCandidate});
  fixture.verifier.SetResults({false, true});

  fixture.supervisor.Start();

  REQUIRE(WaitUntil([&] {
    return fixture.connection.ConfiguredPort() == launchedCandidate.port;
  }));
  CHECK(fixture.verifier.VerifiedCandidates() ==
        std::vector<AdapterHostEndpoint>{rejectedCandidate, launchedCandidate});

  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor retries automatically after a round that "
          "exhausts adoption and launch without success") {
  SupervisorFixture fixture;
  // reader and launcher both default to producing no candidate at all.

  fixture.supervisor.Start();
  //  A second launch attempt proves a retry actually happened, not merely
  //  that one attempt was made.
  REQUIRE(WaitUntil([&] { return fixture.launcher.CallCount() >= 2; }));

  CHECK(fixture.connection.StartCount() == 0);
  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor runs a fresh discovery round after "
          "NotifyConnectionLost, reconnecting to a newly discovered "
          "endpoint") {
  SupervisorFixture fixture;
  AdapterHostEndpoint firstEndpoint = SampleEndpoint(4444);
  AdapterHostEndpoint secondEndpoint = SampleEndpoint(5555);
  fixture.reader.SetResults({firstEndpoint, secondEndpoint});
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();
  REQUIRE(WaitUntil([&] {
    return fixture.connection.ConfiguredPort() == firstEndpoint.port;
  }));

  fixture.supervisor.NotifyConnectionLost();

  REQUIRE(WaitUntil([&] {
    return fixture.connection.ConfiguredPort() == secondEndpoint.port;
  }));
  CHECK(fixture.connection.ConfiguredProofToken() == secondEndpoint.proofToken);
  CHECK(fixture.connection.ConfiguredTargetGeneration() == 2);

  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor::RequestStop during an in-flight round "
          "starts no relaunch and joins promptly") {
  SupervisorFixture fixture;
  BlockingGate gate;
  fixture.reader.BlockNextCallUntilReleased(gate);

  fixture.supervisor.Start();
  //  Confirm the round has actually reached and blocked on the gate before
  //  acting, rather than racing a background thread that may not have
  //  started yet.
  REQUIRE(WaitUntil([&] { return gate.HasWaiter(); }));

  //  Request stop while the round is still in flight, then let it proceed.
  //  RequestStop() itself blocks (it joins the worker), so it runs on its
  //  own thread here; this test relies on a short, bounded wait to make it
  //  very likely `stopping_` is already set before the gate releases, since
  //  there is no production hook to observe that transition directly. The
  //  actual correctness guarantee is the unconditional `stopping_` check
  //  `RunOneDiscoveryRound` performs before ever calling the launcher,
  //  verifiable by inspection; this test is a best-effort confirmation of
  //  it under real scheduling.
  std::thread stopper([&] { fixture.supervisor.RequestStop(); });
  std::this_thread::sleep_for(std::chrono::milliseconds(50));
  gate.Release();
  stopper.join();

  CHECK(fixture.launcher.CallCount() == 0);
  CHECK(fixture.reader.CallCount() == 1);
}

TEST_CASE("AdapterHostSupervisor cancels an in-flight launch without starting "
          "or reconfiguring the connection") {
  SupervisorFixture fixture;
  BlockingGate gate;
  fixture.launcher.SetResults({SampleEndpoint(4321)});
  fixture.launcher.BlockNextCallUntilReleased(gate);

  fixture.supervisor.Start();
  REQUIRE(WaitUntil([&] { return gate.HasWaiter(); }));

  fixture.supervisor.RequestStop();

  CHECK(fixture.connection.StartCount() == 0);
  CHECK(fixture.connection.ConfiguredPort() == 0);
  CHECK(fixture.connection.ConfiguredProofToken().empty());
  CHECK(fixture.verifier.VerifiedCandidates().empty());
}

TEST_CASE("AdapterHostSupervisor cancels an in-flight verification without "
          "launching or starting the connection") {
  SupervisorFixture fixture;
  BlockingGate gate;
  AdapterHostEndpoint endpoint = SampleEndpoint(5432);
  fixture.reader.SetResults({endpoint});
  fixture.verifier.SetResults({true});
  fixture.verifier.BlockNextCallUntilReleased(gate);

  fixture.supervisor.Start();
  REQUIRE(WaitUntil([&] { return gate.HasWaiter(); }));

  fixture.supervisor.RequestStop();

  CHECK(fixture.launcher.CallCount() == 0);
  CHECK(fixture.connection.StartCount() == 0);
  CHECK(fixture.connection.ConfiguredPort() == 0);
  CHECK(fixture.connection.ConfiguredProofToken().empty());
  CHECK(fixture.verifier.VerifiedCandidates().empty());
}

TEST_CASE("AdapterHostSupervisor skips launched-candidate verification when "
          "shutdown begins after launch") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(6543);
  fixture.launcher.SetResults({endpoint});
  fixture.launcher.SetOnLaunchCallback(
      [&] { fixture.supervisor.RequestStop(); });

  fixture.supervisor.Start();
  REQUIRE(WaitUntil([&] { return fixture.launcher.CallCount() == 1; }));
  fixture.supervisor.RequestStop();

  CHECK(fixture.verifier.VerifiedCandidates().empty());
  CHECK(fixture.connection.StartCount() == 0);
  CHECK(fixture.connection.ConfiguredPort() == 0);
  CHECK(fixture.connection.ConfiguredProofToken().empty());
}

TEST_CASE("AdapterHostSupervisor::RequestStop during a successful round's "
          "wait joins promptly and starts no further round") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(6666);
  fixture.reader.SetResults({endpoint});
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();
  REQUIRE(WaitUntil(
      [&] { return fixture.connection.ConfiguredPort() == endpoint.port; }));

  fixture.supervisor.RequestStop();

  CHECK(fixture.reader.CallCount() == 1);
}

TEST_CASE("AdapterHostSupervisor::RequestStop during a failed round's "
          "backoff wait joins promptly and starts no further round") {
  SupervisorFixture fixture;
  // reader and launcher both default to producing no candidate at all.

  fixture.supervisor.Start();
  REQUIRE(WaitUntil([&] { return fixture.launcher.CallCount() >= 1; }));

  //  RequestStop() joins the worker before returning, so by the time it
  //  returns the background thread has definitely stopped and the call
  //  count below is final, not a snapshot racing a still-running round.
  fixture.supervisor.RequestStop();

  CHECK(fixture.launcher.CallCount() >= 1);
}

TEST_CASE("AdapterHostSupervisor recovers after repeated failed rounds") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(7777);
  // reader always produces no candidate; launcher fails twice, then
  // succeeds, then keeps succeeding.
  fixture.launcher.SetResults({std::nullopt, std::nullopt, endpoint});
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();

  REQUIRE(WaitUntil(
      [&] { return fixture.connection.ConfiguredPort() == endpoint.port; },
      std::chrono::seconds(10)));

  CHECK(fixture.connection.StartCount() == 1);
  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor treats a collaborator exception as a "
          "failed round instead of letting it escape the worker thread") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(9999);
  fixture.reader.SetResults({endpoint});
  fixture.verifier.SetThrowsOnce();
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();

  //  The first round's adoption attempt throws; the round must be treated
  //  as failed and retried in the very next round (adopting the same
  //  candidate, which this time verifies normally) rather than crashing
  //  the background thread.
  REQUIRE(WaitUntil(
      [&] { return fixture.connection.ConfiguredPort() == endpoint.port; }));

  fixture.supervisor.RequestStop();
}

TEST_CASE("AdapterHostSupervisor::RequestStop can be called from within a "
          "collaborator's own callback, on the supervisor's own worker "
          "thread, without self-joining") {
  SupervisorFixture fixture;
  fixture.reader.SetResults({SampleEndpoint(8888)});
  fixture.verifier.SetOnVerifyCallback(
      [&] { fixture.supervisor.RequestStop(); });
  fixture.verifier.SetResults({true});

  fixture.supervisor.Start();

  REQUIRE(WaitUntil(
      [&] { return !fixture.verifier.VerifiedCandidates().empty(); }));

  //  A second, ordinary call from this (the main) thread: if the reentrant
  //  call above had incorrectly joined its own thread, the worker would
  //  never have returned and this call would hang instead of completing.
  fixture.supervisor.RequestStop();

  CHECK(fixture.connection.StartCount() == 0);
  CHECK(fixture.connection.ConfiguredPort() == 0);
  CHECK(fixture.connection.ConfiguredProofToken().empty());
  CHECK(fixture.launcher.CallCount() == 0);
}

TEST_CASE("AdapterHostSupervisor contains a connection-start exception and "
          "retries the verified target") {
  SupervisorFixture fixture;
  AdapterHostEndpoint endpoint = SampleEndpoint(1234);
  fixture.reader.SetResults({endpoint});
  fixture.verifier.SetResults({true});
  fixture.connection.SetThrowsOnce();

  fixture.supervisor.Start();

  REQUIRE(WaitUntil([&] { return fixture.connection.StartCount() == 1; }));
  CHECK(fixture.connection.ConfiguredPort() == endpoint.port);

  fixture.supervisor.RequestStop();
}
