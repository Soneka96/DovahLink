#include "application/coordinator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <semaphore>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::CallbackRegistry;
using dovahlink::application::ContainedWorkRunner;
using dovahlink::application::Coordinator;
using dovahlink::application::TransportLifecycle;
using dovahlink::application::WorkerPool;

namespace {

/// Provides a callback registry with no observable work for failure-path tests.
class NoopCallbackRegistry : public CallbackRegistry {
public:
    /// @copydoc CallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner callbackRunner) override { callbackRunner_ = std::move(callbackRunner); }
    /// @copydoc CallbackRegistry::UnregisterAll
    void UnregisterAll() override {
        if (unregisterSignal_ != nullptr) {
            unregisterSignal_->release();
        }
    }

    /// Callback boundary received during coordinator startup.
    ContainedWorkRunner callbackRunner_;

    /// Optional synchronization point released when shutdown unregisters callbacks.
    std::binary_semaphore* unregisterSignal_ = nullptr;
};

/// Provides a worker pool with no observable work for failure-path tests.
class NoopWorkerPool : public WorkerPool {
public:
    /// @copydoc WorkerPool::Start
    void Start(ContainedWorkRunner workerRunner) override { workerRunner_ = std::move(workerRunner); }
    /// @copydoc WorkerPool::Stop
    void Stop() override {
        if (completionOrder_ != nullptr) {
            stopOrder_ = completionOrder_->fetch_add(1, std::memory_order_relaxed) + 1;
        }
    }
    /// @copydoc WorkerPool::Join
    void Join() override {}

    /// Worker boundary received during coordinator startup.
    ContainedWorkRunner workerRunner_;

    /// Optional shared sequence used to order callback completion and worker stopping.
    std::atomic<int>* completionOrder_ = nullptr;

    /// Sequence assigned when worker stopping begins.
    int stopOrder_ = 0;
};

/// Provides a transport lifecycle with no observable work for failure-path tests.
class NoopTransportLifecycle : public TransportLifecycle {
public:
    /// @copydoc TransportLifecycle::Start
    void Start() override {}
    /// @copydoc TransportLifecycle::CancelCompletions
    void CancelCompletions() override {}
    /// @copydoc TransportLifecycle::Close
    void Close() override {}
};

/// Bundles inert coordinator dependencies for each failure-path test.
struct Fixture {
    /// Records no callback operations.
    NoopCallbackRegistry callbacks;
    /// Records no worker-pool operations.
    NoopWorkerPool workers;
    /// Records no transport operations.
    NoopTransportLifecycle transport;
    /// Coordinator under test.
    Coordinator coordinator{callbacks, workers, transport};
};

}  // namespace

TEST_CASE("a fresh coordinator is available", "[application][coordinator][failure]") {
    Fixture f;
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained returns true and stays available when work does not throw",
          "[application][coordinator][failure]") {
    Fixture f;
    bool ran = false;
    bool result = f.coordinator.RunContained([&ran] { ran = true; });

    CHECK(result);
    CHECK(ran);
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a std::exception thrown from a callback boundary and marks unavailable",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("callback failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches an exception thrown from a worker-thread boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("worker loop failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches an exception thrown from a transport-completion boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("transport completion failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a non-std::exception throwable", "[application][coordinator][failure]") {
    // The "never allow an exception to escape" rule applies to anything
    // throwable, not only std::exception subclasses.
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw 42; });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("Start gives workers a catch-all coordinator boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Start();
    REQUIRE(f.workers.workerRunner_);

    bool result = f.workers.workerRunner_([] { throw std::runtime_error("session failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("Start gives callbacks a guarded catch-all coordinator boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Start();
    REQUIRE(f.callbacks.callbackRunner_);

    bool result = f.callbacks.callbackRunner_([] { throw 42; });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("the callback boundary refuses late work after shutdown",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Start();
    REQUIRE(f.callbacks.callbackRunner_);
    ContainedWorkRunner callbackRunner = f.callbacks.callbackRunner_;
    f.coordinator.Shutdown();

    bool ran = false;
    bool result = callbackRunner([&ran] { ran = true; });

    CHECK_FALSE(result);
    CHECK_FALSE(ran);
}

TEST_CASE("the callback boundary keeps shutdown behind admitted work",
          "[application][coordinator][failure]") {
    Fixture f;
    std::binary_semaphore callbackEntered{0};
    std::binary_semaphore releaseCallback{0};
    std::binary_semaphore callbacksUnregistered{0};
    std::atomic<int> completionOrder{0};
    int callbackCompletionOrder = 0;
    bool callbackResult = false;
    f.callbacks.unregisterSignal_ = &callbacksUnregistered;
    f.workers.completionOrder_ = &completionOrder;
    f.coordinator.Start();
    REQUIRE(f.callbacks.callbackRunner_);

    std::thread callbackThread([&] {
        callbackResult = f.callbacks.callbackRunner_([&] {
            callbackEntered.release();
            releaseCallback.acquire();
            callbackCompletionOrder = completionOrder.fetch_add(1, std::memory_order_relaxed) + 1;
        });
    });
    callbackEntered.acquire();

    std::thread shutdownThread([&] { f.coordinator.Shutdown(); });
    callbacksUnregistered.acquire();
    releaseCallback.release();

    callbackThread.join();
    shutdownThread.join();

    CHECK(callbackResult);
    CHECK(callbackCompletionOrder == 1);
    CHECK(f.workers.stopOrder_ == 2);
}

TEST_CASE("ResetAvailability restores availability after a failure",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    f.coordinator.ResetAvailability();
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("a second failure after ResetAvailability marks the coordinator unavailable again",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("first failure"); });
    f.coordinator.ResetAvailability();
    REQUIRE(f.coordinator.IsAvailable());

    f.coordinator.RunContained([] { throw std::runtime_error("second failure"); });
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("multiple RegisterFailure calls without a reset between them are safe",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();

    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a later successful RunContained call does not restore availability on its own",
          "[application][coordinator][failure]") {
    // Availability is only restored by an explicit ResetAvailability call once
    // a fresh snapshot is published on a reconnected session -- a merely
    // successful, unrelated piece of contained work must not silently clear
    // an existing failure.
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    bool result = f.coordinator.RunContained([] { /* succeeds */ });

    CHECK(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a failure registered before shutdown does not block or alter the shutdown barrier",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    f.coordinator.Shutdown();  // must complete normally, not hang.

    CHECK(f.coordinator.IsStopping());
    // Shutdown does not itself restore availability; that stays an explicit,
    // reconnect-driven decision (ResetAvailability).
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained after shutdown still contains the exception without hanging or crashing",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    REQUIRE(f.coordinator.IsStopping());

    bool result = f.coordinator.RunContained([] { throw std::runtime_error("late failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("ResetAvailability after shutdown does not affect the already-completed shutdown state",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    f.coordinator.ResetAvailability();

    CHECK(f.coordinator.IsAvailable());
    CHECK(f.coordinator.IsStopping());  // shutdown remains in effect regardless.
}

TEST_CASE("RunContained does not let an exception propagate past it", "[application][coordinator][failure]") {
    // If this were not caught, the test itself would fail with an unhandled
    // exception rather than reaching the checks below.
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("must not escape"); });
    SUCCEED("RunContained returned normally instead of propagating the exception");
}
