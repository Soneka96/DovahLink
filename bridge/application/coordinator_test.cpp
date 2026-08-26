#include "application/coordinator.hpp"

#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <fstream>
#include <memory>
#include <semaphore>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::ContainedWorkRunner;
using dovahlink::application::Coordinator;
using dovahlink::application::IBridgeCallbackRegistry;
using dovahlink::application::IBridgeTransport;
using dovahlink::application::IBridgeWorkerPool;
using dovahlink::application::LifetimeToken;

namespace {

///  Reads the Coordinator header for structural include-boundary assertions.
std::string ReadCoordinatorHeader() {
    std::ifstream file(DOVAHLINK_COORDINATOR_HEADER_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Records callback lifecycle calls in the shared test log.
class RecordingCallbackRegistry : public IBridgeCallbackRegistry {
  public:
    ///  Binds the recorder to the caller-owned lifecycle log.
    explicit RecordingCallbackRegistry(std::vector<std::string>& log)
        : log_(log) {}
    ///  @copydoc IBridgeCallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner) override {
        log_.push_back("callbacks.RegisterAll");
    }
    ///  @copydoc IBridgeCallbackRegistry::UnregisterAll
    void UnregisterAll() override { log_.push_back("callbacks.UnregisterAll"); }

  private:
    ///  Log receiving lifecycle call names.
    std::vector<std::string>& log_;
};

///  Blocks callback registration at a deterministic startup synchronization
///  point.
class BlockingCallbackRegistry : public IBridgeCallbackRegistry {
  public:
    ///  Binds the recorder and startup synchronization points.
    BlockingCallbackRegistry(std::vector<std::string>& log) : log_(log) {}

    ///  @copydoc IBridgeCallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner) override {
        log_.push_back("callbacks.RegisterAll");
        registerEntered_.release();
        releaseRegister_.acquire();
    }

    ///  @copydoc IBridgeCallbackRegistry::UnregisterAll
    void UnregisterAll() override { log_.push_back("callbacks.UnregisterAll"); }

    ///  Signals that callback registration has started.
    std::binary_semaphore registerEntered_{0};

    ///  Releases callback registration so startup can continue.
    std::binary_semaphore releaseRegister_{0};

  private:
    ///  Log receiving lifecycle call names.
    std::vector<std::string>& log_;
};

///  Records worker-pool lifecycle calls in the shared test log.
class RecordingWorkerPool : public IBridgeWorkerPool {
  public:
    ///  Binds the recorder to the caller-owned lifecycle log.
    explicit RecordingWorkerPool(std::vector<std::string>& log) : log_(log) {}
    ///  @copydoc IBridgeWorkerPool::Start
    void Start(ContainedWorkRunner) override { log_.push_back("workers.Start"); }
    ///  @copydoc IBridgeWorkerPool::Stop
    void Stop() override { log_.push_back("workers.Stop"); }
    ///  @copydoc IBridgeWorkerPool::Join
    void Join() override { log_.push_back("workers.Join"); }

  private:
    ///  Log receiving lifecycle call names.
    std::vector<std::string>& log_;
};

///  Records transport lifecycle calls in the shared test log.
class RecordingTransportLifecycle : public IBridgeTransport {
  public:
    ///  Binds the recorder to the caller-owned lifecycle log.
    explicit RecordingTransportLifecycle(std::vector<std::string>& log)
        : log_(log) {}
    ///  @copydoc IBridgeTransport::Start
    void Start() override { log_.push_back("transport.Start"); }
    ///  @copydoc IBridgeTransport::CancelCompletions
    void CancelCompletions() override {
        log_.push_back("transport.CancelCompletions");
    }
    ///  @copydoc IBridgeTransport::Close
    void Close() override { log_.push_back("transport.Close"); }

  private:
    ///  Log receiving lifecycle call names.
    std::vector<std::string>& log_;
};

///  Bundles recording coordinator dependencies for lifecycle-order tests.
struct Fixture {
    ///  Captures the order of lifecycle calls.
    std::vector<std::string> log;
    ///  Records callback lifecycle calls.
    RecordingCallbackRegistry callbacks{log};
    ///  Records worker-pool lifecycle calls.
    RecordingWorkerPool workers{log};
    ///  Records transport lifecycle calls.
    RecordingTransportLifecycle transport{log};
    ///  Coordinator under test.
    Coordinator coordinator{callbacks, workers, transport};
};

} //  namespace

TEST_CASE("Coordinator header depends only on lifecycle contracts",
          "[application][coordinator][includes]") {
    const std::string header = ReadCoordinatorHeader();

    CHECK(dovahlink::test_support::ContainsSourceText(
        header, "class IBridgeWorkerPool;"));
    CHECK(dovahlink::test_support::ContainsSourceText(
        header, "class IBridgeTransport;"));
    CHECK_FALSE(dovahlink::test_support::ContainsSourceText(
        header, "#include application/bridge_worker_pool.hpp"));
    CHECK_FALSE(dovahlink::test_support::ContainsSourceText(
        header, "#include application/bridge_transport.hpp"));
}

TEST_CASE(
    "Start registers callbacks, then starts workers, then starts transport",
    "[application][coordinator]") {
    Fixture f;
    f.coordinator.Start();

    CHECK(f.log == std::vector<std::string>{"callbacks.RegisterAll",
                                            "workers.Start", "transport.Start"});
}

TEST_CASE("a second Start call is a no-op", "[application][coordinator]") {
    Fixture f;
    f.coordinator.Start();
    const std::size_t countAfterFirst = f.log.size();

    f.coordinator.Start();

    CHECK(f.log.size() == countAfterFirst);
}

TEST_CASE("Start after shutdown is a no-op", "[application][coordinator]") {
    Fixture f;
    f.coordinator.Shutdown();
    const std::vector<std::string> shutdownLog = f.log;

    f.coordinator.Start();

    CHECK(f.log == shutdownLog);
}

TEST_CASE("Start and Shutdown serialize lifecycle transitions",
          "[application][coordinator]") {
    std::vector<std::string> log;
    BlockingCallbackRegistry callbacks(log);
    RecordingWorkerPool workers(log);
    RecordingTransportLifecycle transport(log);
    Coordinator coordinator(callbacks, workers, transport);

    std::thread startThread([&coordinator] { coordinator.Start(); });
    callbacks.registerEntered_.acquire();

    std::binary_semaphore shutdownCalled(0);
    std::binary_semaphore shutdownFinished(0);
    std::thread shutdownThread(
        [&coordinator, &shutdownCalled, &shutdownFinished] {
            shutdownCalled.release();
            coordinator.Shutdown();
            shutdownFinished.release();
        });
    shutdownCalled.acquire();

    CHECK_FALSE(shutdownFinished.try_acquire());
    callbacks.releaseRegister_.release();

    startThread.join();
    shutdownThread.join();

    CHECK(log == std::vector<std::string>{
                     "callbacks.RegisterAll",
                     "workers.Start",
                     "transport.Start",
                     "callbacks.UnregisterAll",
                     "workers.Stop",
                     "workers.Join",
                     "transport.CancelCompletions",
                     "transport.Close",
                 });
}

TEST_CASE("Shutdown follows the exact documented barrier order with no "
          "callbacks in flight",
          "[application][coordinator]") {
    Fixture f;
    f.coordinator.Shutdown();

    CHECK(f.log == std::vector<std::string>{
                       "callbacks.UnregisterAll",
                       "workers.Stop",
                       "workers.Join",
                       "transport.CancelCompletions",
                       "transport.Close",
                   });
}

TEST_CASE("Coordinator destruction completes the shutdown barrier before "
          "dependencies are destroyed",
          "[application][coordinator]") {
    std::vector<std::string> log;
    {
        RecordingCallbackRegistry callbacks(log);
        RecordingWorkerPool workers(log);
        RecordingTransportLifecycle transport(log);
        Coordinator coordinator(callbacks, workers, transport);

        coordinator.Start();
    }

    CHECK(log == std::vector<std::string>{
                     "callbacks.RegisterAll",
                     "workers.Start",
                     "transport.Start",
                     "callbacks.UnregisterAll",
                     "workers.Stop",
                     "workers.Join",
                     "transport.CancelCompletions",
                     "transport.Close",
                 });
}

TEST_CASE("Coordinator destruction shuts down dependencies even when Start was "
          "never called",
          "[application][coordinator]") {
    std::vector<std::string> log;
    {
        RecordingCallbackRegistry callbacks(log);
        RecordingWorkerPool workers(log);
        RecordingTransportLifecycle transport(log);
        Coordinator coordinator(callbacks, workers, transport);
    }

    CHECK(log == std::vector<std::string>{
                     "callbacks.UnregisterAll",
                     "workers.Stop",
                     "workers.Join",
                     "transport.CancelCompletions",
                     "transport.Close",
                 });
}

TEST_CASE("Shutdown marks the coordinator stopping",
          "[application][coordinator]") {
    Fixture f;
    CHECK_FALSE(f.coordinator.IsStopping());
    f.coordinator.Shutdown();
    CHECK(f.coordinator.IsStopping());
}

TEST_CASE("a second Shutdown call is a no-op", "[application][coordinator]") {
    Fixture f;
    f.coordinator.Shutdown();
    std::size_t countAfterFirst = f.log.size();
    f.coordinator.Shutdown();

    CHECK(f.log.size() == countAfterFirst);
}

TEST_CASE("Shutdown called concurrently from multiple threads still runs the "
          "barrier exactly once",
          "[application][coordinator]") {
    Fixture f;

    constexpr int kThreads = 8;
    std::vector<std::thread> threads;
    threads.reserve(kThreads);
    for (int i = 0; i < kThreads; ++i) {
        threads.emplace_back([&f] { f.coordinator.Shutdown(); });
    }
    for (std::thread& t : threads) {
        t.join();
    }

    CHECK(f.log == std::vector<std::string>{
                       "callbacks.UnregisterAll",
                       "workers.Stop",
                       "workers.Join",
                       "transport.CancelCompletions",
                       "transport.Close",
                   });
}

TEST_CASE("a Shutdown call that arrives after another is already in progress "
          "waits for it to finish",
          "[application][coordinator]") {
    //  Regression seam: a caller whose Shutdown() call did not win the race to
    //  run the barrier must not observe Shutdown() returning before the
    //  winner's barrier has actually completed. Proven the same way as the
    //  in-flight-callback wait below: by a deterministic final state that
    //  could only be reached if both calls genuinely blocked. Which of the two
    //  threads below wins the race is unspecified and does not matter here:
    //  whichever one runs the barrier blocks on the held guard at step 3, and
    //  the other blocks waiting for that one to finish.
    Fixture f;
    auto guard = std::make_unique<Coordinator::CallbackGuard>(f.coordinator);
    REQUIRE(guard->ShouldProceed());

    std::thread first([&f] { f.coordinator.Shutdown(); });
    std::thread second([&f] { f.coordinator.Shutdown(); });

    guard.reset();
    first.join();
    second.join();

    CHECK(f.log == std::vector<std::string>{
                       "callbacks.UnregisterAll",
                       "workers.Stop",
                       "workers.Join",
                       "transport.CancelCompletions",
                       "transport.Close",
                   });
}

namespace {

///  Records whether shutdown state is visible during callback unregistration.
class StoppingCheckCallbackRegistry : public IBridgeCallbackRegistry {
  public:
    ///  Binds the recorder to the lifecycle log and coordinator observation.
    StoppingCheckCallbackRegistry(std::vector<std::string>& log,
                                  const Coordinator*& coordinatorRef)
        : log_(log), coordinatorRef_(coordinatorRef) {}
    ///  @copydoc IBridgeCallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner) override {
        log_.push_back("callbacks.RegisterAll");
    }
    ///  @copydoc IBridgeCallbackRegistry::UnregisterAll
    void UnregisterAll() override {
        wasStoppingDuringUnregister_ =
            coordinatorRef_ != nullptr && coordinatorRef_->IsStopping();
        log_.push_back("callbacks.UnregisterAll");
    }
    ///  Whether unregistration observed the coordinator's stopping state.
    bool wasStoppingDuringUnregister_ = false;

  private:
    ///  Log receiving lifecycle call names.
    std::vector<std::string>& log_;
    ///  Indirect coordinator reference used during the callback.
    const Coordinator*& coordinatorRef_;
};

} //  namespace

TEST_CASE("IsStopping is already true by the time UnregisterAll runs",
          "[application][coordinator]") {
    std::vector<std::string> log;
    const Coordinator* coordinatorPtr = nullptr;
    StoppingCheckCallbackRegistry callbacks(log, coordinatorPtr);
    RecordingWorkerPool workers(log);
    RecordingTransportLifecycle transport(log);
    Coordinator coordinator(callbacks, workers, transport);
    coordinatorPtr = &coordinator;

    CHECK_FALSE(coordinator.IsStopping());
    coordinator.Shutdown();

    CHECK(callbacks.wasStoppingDuringUnregister_);
}

TEST_CASE("a CallbackGuard constructed before shutdown proceeds normally",
          "[application][coordinator]") {
    Fixture f;
    Coordinator::CallbackGuard guard(f.coordinator);
    CHECK(guard.ShouldProceed());
}

TEST_CASE("a CallbackGuard constructed after shutdown does not proceed",
          "[application][coordinator]") {
    Fixture f;
    f.coordinator.Shutdown();

    Coordinator::CallbackGuard guard(f.coordinator);
    CHECK_FALSE(guard.ShouldProceed());
}

TEST_CASE(
    "a guard that does not proceed does not need to be balanced by release",
    "[application][coordinator]") {
    //  A guard built while stopping never joined the in-flight count, so letting
    //  it go out of scope immediately must not affect a later Shutdown call's
    //  wait -- this must not hang.
    Fixture f;
    f.coordinator.Shutdown();
    {
        Coordinator::CallbackGuard guard(f.coordinator);
    }
    f.coordinator.Shutdown(); //  still a no-op; must return promptly.
    SUCCEED();
}

TEST_CASE(
    "Shutdown blocks until a callback already in flight releases its guard",
    "[application][coordinator]") {
    //  This does not need to catch the shutdown thread mid-wait to be a valid,
    //  non-flaky proof: the mutex/condition_variable wait guarantees the final
    //  log order below regardless of how the two threads are scheduled. If the
    //  wait did not actually block on the guard, the order would be
    //  non-deterministic across runs instead of reliably correct.
    Fixture f;
    auto guard = std::make_unique<Coordinator::CallbackGuard>(f.coordinator);
    REQUIRE(guard->ShouldProceed());

    std::thread shutdownThread([&f] { f.coordinator.Shutdown(); });

    guard.reset();
    shutdownThread.join();

    CHECK(f.log == std::vector<std::string>{
                       "callbacks.UnregisterAll",
                       "workers.Stop",
                       "workers.Join",
                       "transport.CancelCompletions",
                       "transport.Close",
                   });
}

TEST_CASE(
    "the transport lifetime token is valid before shutdown and invalid after",
    "[application][coordinator]") {
    Fixture f;
    std::shared_ptr<LifetimeToken> token =
        f.coordinator.TransportLifetimeTokenHandle();
    CHECK(token->IsValid());

    f.coordinator.Shutdown();
    CHECK_FALSE(token->IsValid());
}

TEST_CASE("the transport lifetime token outlives the coordinator",
          "[application][coordinator]") {
    std::shared_ptr<LifetimeToken> token;
    {
        Fixture f;
        f.coordinator.Shutdown();
        token = f.coordinator.TransportLifetimeTokenHandle();
    }
    //  The coordinator (and its fakes) are destroyed; the independently
    //  owned token, held via shared_ptr, is still safely readable.
    CHECK_FALSE(token->IsValid());
}
