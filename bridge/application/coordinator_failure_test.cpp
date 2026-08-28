#include "application/coordinator.hpp"

#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/cadence_tick_driver.hpp"
#include "application/capture_dispatch_worker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <atomic>
#include <chrono>
#include <future>
#include <memory>
#include <semaphore>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::CaptureWorkItem;
using dovahlink::application::ContainedWorkRunner;
using dovahlink::application::Coordinator;
using dovahlink::application::IBridgeCallbackRegistry;
using dovahlink::application::IBridgeTransport;
using dovahlink::application::IBridgeWorkerPool;
using dovahlink::application::ICadenceTickDriver;
using dovahlink::application::ICaptureDispatchWorker;

namespace {

///  Identifies one lifecycle stage configured to fail during a shutdown test.
enum class ShutdownFailureStage {
    ///  No lifecycle stage throws.
    kNone,
    ///  Callback unregistration throws.
    kUnregisterCallbacks,
    ///  Cadence-driver stopping throws.
    kStopCadence,
    ///  Cadence-driver joining throws.
    kJoinCadence,
    ///  Capture-worker stopping throws.
    kStopCapture,
    ///  Capture-worker joining throws.
    kJoinCapture,
    ///  Worker stopping throws.
    kStopWorkers,
    ///  Worker joining throws.
    kJoinWorkers,
    ///  Transport completion cancellation throws.
    kCancelTransportCompletions,
    ///  Transport closure throws a non-standard value.
    kCloseTransport,
};

///  Throws when the current lifecycle stage is the configured failure point.
///  @param configuredStage Stage selected by the test.
///  @param currentStage Stage being executed.
void ThrowAtStage(ShutdownFailureStage configuredStage,
                  ShutdownFailureStage currentStage) {
    if (configuredStage != currentStage) {
        return;
    }
    if (currentStage == ShutdownFailureStage::kCloseTransport) {
        throw 42;
    }
    throw std::runtime_error("configured shutdown failure");
}

///  Provides a callback registry with configurable shutdown behavior.
class NoopCallbackRegistry : public IBridgeCallbackRegistry {
  public:
    ///  @copydoc IBridgeCallbackRegistry::RegisterAll
    void RegisterAll(ContainedWorkRunner callbackRunner) override {
        ++startCalls_;
        if (throwOnStart_) {
            throw std::runtime_error("configured callback startup failure");
        }
        callbackRunner_ = std::move(callbackRunner);
    }
    ///  @copydoc IBridgeCallbackRegistry::UnregisterAll
    void UnregisterAll() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("callbacks.UnregisterAll");
        }
        if (unregisterSignal_ != nullptr) {
            unregisterSignal_->release();
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kUnregisterCallbacks);
    }

    ///  Callback boundary received during coordinator startup.
    ContainedWorkRunner callbackRunner_;

    ///  Number of callback startup attempts received.
    int startCalls_ = 0;

    ///  Whether callback startup should throw.
    bool throwOnStart_ = false;

    ///  Optional synchronization point released when shutdown unregisters
    ///  callbacks.
    std::binary_semaphore* unregisterSignal_ = nullptr;

    ///  Optional log receiving shutdown stage names.
    std::vector<std::string>* shutdownLog_ = nullptr;

    ///  Lifecycle stage configured to throw.
    ShutdownFailureStage failureStage_ = ShutdownFailureStage::kNone;
};

///  Provides a worker pool with configurable shutdown behavior.
class NoopWorkerPool : public IBridgeWorkerPool {
  public:
    ///  @copydoc IBridgeWorkerPool::Start
    void Start(ContainedWorkRunner workerRunner) override {
        ++startCalls_;
        if (throwOnStart_) {
            throw std::runtime_error("configured worker startup failure");
        }
        workerRunner_ = std::move(workerRunner);
    }
    ///  @copydoc IBridgeWorkerPool::Stop
    void Stop() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("workers.Stop");
        }
        if (completionOrder_ != nullptr) {
            stopOrder_ =
                completionOrder_->fetch_add(1, std::memory_order_relaxed) + 1;
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kStopWorkers);
    }
    ///  @copydoc IBridgeWorkerPool::Join
    void Join() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("workers.Join");
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kJoinWorkers);
    }

    ///  Worker boundary received during coordinator startup.
    ContainedWorkRunner workerRunner_;

    ///  Number of worker startup attempts received.
    int startCalls_ = 0;

    ///  Whether worker startup should throw.
    bool throwOnStart_ = false;

    ///  Optional shared sequence used to order callback completion and worker
    ///  stopping.
    std::atomic<int>* completionOrder_ = nullptr;

    ///  Sequence assigned when worker stopping begins.
    int stopOrder_ = 0;

    ///  Optional log receiving shutdown stage names.
    std::vector<std::string>* shutdownLog_ = nullptr;

    ///  Lifecycle stage configured to throw.
    ShutdownFailureStage failureStage_ = ShutdownFailureStage::kNone;
};

///  Provides a transport lifecycle with configurable shutdown behavior.
class NoopTransportLifecycle : public IBridgeTransport {
  public:
    ///  @copydoc IBridgeTransport::Start
    void Start() override {
        ++startCalls_;
        if (throwOnStart_) {
            throw std::runtime_error("configured transport startup failure");
        }
    }
    ///  @copydoc IBridgeTransport::CancelCompletions
    void CancelCompletions() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("transport.CancelCompletions");
        }
        ThrowAtStage(failureStage_,
                     ShutdownFailureStage::kCancelTransportCompletions);
    }
    ///  @copydoc IBridgeTransport::Close
    void Close() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("transport.Close");
        }
        if (closeEntered_ != nullptr) {
            closeEntered_->release();
        }
        if (releaseClose_ != nullptr) {
            releaseClose_->acquire();
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kCloseTransport);
    }

    ///  Optional log receiving shutdown stage names.
    std::vector<std::string>* shutdownLog_ = nullptr;

    ///  Number of transport startup attempts received.
    int startCalls_ = 0;

    ///  Whether transport startup should throw.
    bool throwOnStart_ = false;

    ///  Lifecycle stage configured to throw.
    ShutdownFailureStage failureStage_ = ShutdownFailureStage::kNone;

    ///  Optional synchronization point released when transport closure begins.
    std::binary_semaphore* closeEntered_ = nullptr;

    ///  Optional synchronization point blocking transport closure until released.
    std::binary_semaphore* releaseClose_ = nullptr;
};

///  Provides a capture worker with configurable startup behavior.
class NoopCaptureDispatchWorker : public ICaptureDispatchWorker {
  public:
    ///  @copydoc ICaptureDispatchWorker::Start
    void Start() override {
        ++startCalls_;
        if (throwOnStart_) {
            throw std::runtime_error("configured capture startup failure");
        }
    }
    ///  @copydoc ICaptureDispatchWorker::Stop
    void Stop() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("capture.Stop");
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kStopCapture);
    }
    ///  @copydoc ICaptureDispatchWorker::Join
    void Join() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("capture.Join");
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kJoinCapture);
    }
    ///  @copydoc ICaptureDispatchWorker::TryEnqueue
    bool TryEnqueue(CaptureWorkItem) override { return false; }

    ///  Number of capture-worker startup attempts.
    int startCalls_ = 0;
    ///  Whether capture-worker startup should throw.
    bool throwOnStart_ = false;
    ///  Optional log receiving shutdown stage names.
    std::vector<std::string>* shutdownLog_ = nullptr;
    ///  Lifecycle stage configured to throw.
    ShutdownFailureStage failureStage_ = ShutdownFailureStage::kNone;
};

///  Provides a cadence driver with configurable startup behavior.
class NoopCadenceTickDriver : public ICadenceTickDriver {
  public:
    ///  @copydoc ICadenceTickDriver::Start
    void Start() override {
        ++startCalls_;
        if (throwOnStart_) {
            throw std::runtime_error("configured cadence startup failure");
        }
    }
    ///  @copydoc ICadenceTickDriver::Stop
    void Stop() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("cadence.Stop");
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kStopCadence);
    }
    ///  @copydoc ICadenceTickDriver::Join
    void Join() override {
        if (shutdownLog_ != nullptr) {
            shutdownLog_->push_back("cadence.Join");
        }
        ThrowAtStage(failureStage_, ShutdownFailureStage::kJoinCadence);
    }

    ///  Number of cadence-driver startup attempts.
    int startCalls_ = 0;
    ///  Whether cadence-driver startup should throw.
    bool throwOnStart_ = false;
    ///  Optional log receiving shutdown stage names.
    std::vector<std::string>* shutdownLog_ = nullptr;
    ///  Lifecycle stage configured to throw.
    ShutdownFailureStage failureStage_ = ShutdownFailureStage::kNone;
};

///  Bundles configurable coordinator dependencies for each failure-path test.
struct Fixture {
    ///  Provides callback lifecycle behavior.
    NoopCallbackRegistry callbacks;
    ///  Provides worker-pool lifecycle behavior.
    NoopWorkerPool workers;
    ///  Provides transport lifecycle behavior.
    NoopTransportLifecycle transport;
    ///  Provides capture-worker lifecycle behavior.
    NoopCaptureDispatchWorker captureWorker;
    ///  Provides cadence-driver lifecycle behavior.
    NoopCadenceTickDriver cadenceDriver;
    ///  Coordinator under test.
    Coordinator coordinator{callbacks, workers, transport, captureWorker,
                            cadenceDriver};
};

} //  namespace

TEST_CASE("a callback startup failure latches startup and prevents a retry",
          "[application][coordinator][failure]") {
    Fixture f;
    f.callbacks.throwOnStart_ = true;

    CHECK_THROWS(f.coordinator.Start());
    f.callbacks.throwOnStart_ = false;
    f.workers.throwOnStart_ = false;
    f.transport.throwOnStart_ = false;
    CHECK_NOTHROW(f.coordinator.Start());

    CHECK(f.callbacks.startCalls_ == 1);
    CHECK(f.workers.startCalls_ == 0);
    CHECK(f.transport.startCalls_ == 0);
    CHECK(f.captureWorker.startCalls_ == 0);
    CHECK(f.cadenceDriver.startCalls_ == 0);
}

TEST_CASE("a worker startup failure latches startup and prevents a retry",
          "[application][coordinator][failure]") {
    Fixture f;
    f.workers.throwOnStart_ = true;

    CHECK_THROWS(f.coordinator.Start());
    f.callbacks.throwOnStart_ = false;
    f.workers.throwOnStart_ = false;
    f.transport.throwOnStart_ = false;
    CHECK_NOTHROW(f.coordinator.Start());

    CHECK(f.callbacks.startCalls_ == 1);
    CHECK(f.workers.startCalls_ == 1);
    CHECK(f.transport.startCalls_ == 0);
    CHECK(f.captureWorker.startCalls_ == 0);
    CHECK(f.cadenceDriver.startCalls_ == 0);
}

TEST_CASE("a transport startup failure latches startup and prevents a retry",
          "[application][coordinator][failure]") {
    Fixture f;
    f.transport.throwOnStart_ = true;

    CHECK_THROWS(f.coordinator.Start());
    f.callbacks.throwOnStart_ = false;
    f.workers.throwOnStart_ = false;
    f.transport.throwOnStart_ = false;
    CHECK_NOTHROW(f.coordinator.Start());

    CHECK(f.callbacks.startCalls_ == 1);
    CHECK(f.workers.startCalls_ == 1);
    CHECK(f.transport.startCalls_ == 1);
    CHECK(f.captureWorker.startCalls_ == 0);
    CHECK(f.cadenceDriver.startCalls_ == 0);
}

TEST_CASE("a capture-worker startup failure latches startup and prevents a retry",
          "[application][coordinator][failure]") {
    Fixture f;
    f.captureWorker.throwOnStart_ = true;

    CHECK_THROWS(f.coordinator.Start());
    f.captureWorker.throwOnStart_ = false;
    CHECK_NOTHROW(f.coordinator.Start());

    CHECK(f.callbacks.startCalls_ == 1);
    CHECK(f.workers.startCalls_ == 1);
    CHECK(f.transport.startCalls_ == 1);
    CHECK(f.captureWorker.startCalls_ == 1);
    CHECK(f.cadenceDriver.startCalls_ == 0);
}

TEST_CASE("a cadence startup failure latches startup and prevents a retry",
          "[application][coordinator][failure]") {
    Fixture f;
    f.cadenceDriver.throwOnStart_ = true;

    CHECK_THROWS(f.coordinator.Start());
    f.cadenceDriver.throwOnStart_ = false;
    CHECK_NOTHROW(f.coordinator.Start());

    CHECK(f.callbacks.startCalls_ == 1);
    CHECK(f.workers.startCalls_ == 1);
    CHECK(f.transport.startCalls_ == 1);
    CHECK(f.captureWorker.startCalls_ == 1);
    CHECK(f.cadenceDriver.startCalls_ == 1);
}

TEST_CASE("a fresh coordinator is available",
          "[application][coordinator][failure]") {
    Fixture f;
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE(
    "RunContained returns true and stays available when work does not throw",
    "[application][coordinator][failure]") {
    Fixture f;
    bool ran = false;
    bool result = f.coordinator.RunContained([&ran] { ran = true; });

    CHECK(result);
    CHECK(ran);
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a std::exception thrown from a callback "
          "boundary and marks unavailable",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained(
        [] { throw std::runtime_error("callback failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE(
    "RunContained catches an exception thrown from a worker-thread boundary",
    "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained(
        [] { throw std::runtime_error("worker loop failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches an exception thrown from a "
          "transport-completion boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained(
        [] { throw std::runtime_error("transport completion failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a non-std::exception throwable",
          "[application][coordinator][failure]") {
    //  The "never allow an exception to escape" rule applies to anything
    //  throwable, not only std::exception subclasses.
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

    bool result = f.workers.workerRunner_(
        [] { throw std::runtime_error("session failure"); });

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
            callbackCompletionOrder =
                completionOrder.fetch_add(1, std::memory_order_relaxed) + 1;
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

TEST_CASE("a second failure after ResetAvailability marks the coordinator "
          "unavailable again",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("first failure"); });
    f.coordinator.ResetAvailability();
    REQUIRE(f.coordinator.IsAvailable());

    f.coordinator.RunContained(
        [] { throw std::runtime_error("second failure"); });
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE(
    "multiple RegisterFailure calls without a reset between them are safe",
    "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();

    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a later successful RunContained call does not restore availability "
          "on its own",
          "[application][coordinator][failure]") {
    //  Availability is only restored by an explicit ResetAvailability call once
    //  a fresh snapshot is published on a reconnected session -- a merely
    //  successful, unrelated piece of contained work must not silently clear
    //  an existing failure.
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    bool result = f.coordinator.RunContained([] { /* succeeds */ });

    CHECK(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a failure registered before shutdown does not block or alter the "
          "shutdown barrier",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    f.coordinator.Shutdown(); //  must complete normally, not hang.

    CHECK(f.coordinator.IsStopping());
    //  Shutdown does not itself restore availability; that stays an explicit,
    //  reconnect-driven decision (ResetAvailability).
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("Shutdown contains every lifecycle failure and attempts every later "
          "cleanup stage",
          "[application][coordinator][failure]") {
    constexpr std::array kFailureStages{
        ShutdownFailureStage::kUnregisterCallbacks,
        ShutdownFailureStage::kStopCadence,
        ShutdownFailureStage::kJoinCadence,
        ShutdownFailureStage::kStopCapture,
        ShutdownFailureStage::kJoinCapture,
        ShutdownFailureStage::kStopWorkers,
        ShutdownFailureStage::kJoinWorkers,
        ShutdownFailureStage::kCancelTransportCompletions,
        ShutdownFailureStage::kCloseTransport,
    };
    const std::vector<std::string> expectedLog{
        "cadence.Stop",
        "cadence.Join",
        "callbacks.UnregisterAll",
        "capture.Stop",
        "capture.Join",
        "workers.Stop",
        "workers.Join",
        "transport.CancelCompletions",
        "transport.Close",
    };

    for (ShutdownFailureStage failureStage : kFailureStages) {
        CAPTURE(static_cast<int>(failureStage));
        Fixture f;
        std::vector<std::string> shutdownLog;
        f.callbacks.shutdownLog_ = &shutdownLog;
        f.callbacks.failureStage_ = failureStage;
        f.cadenceDriver.shutdownLog_ = &shutdownLog;
        f.cadenceDriver.failureStage_ = failureStage;
        f.captureWorker.shutdownLog_ = &shutdownLog;
        f.captureWorker.failureStage_ = failureStage;
        f.workers.shutdownLog_ = &shutdownLog;
        f.workers.failureStage_ = failureStage;
        f.transport.shutdownLog_ = &shutdownLog;
        f.transport.failureStage_ = failureStage;
        auto lifetimeToken = f.coordinator.TransportLifetimeTokenHandle();

        f.coordinator.Shutdown();

        CHECK(shutdownLog == expectedLog);
        CHECK_FALSE(f.coordinator.IsAvailable());
        REQUIRE(f.coordinator.IsStopping());
        CHECK_FALSE(lifetimeToken->IsValid());

        f.coordinator.Shutdown();
        CHECK(shutdownLog == expectedLog);
    }
}

TEST_CASE("a concurrent Shutdown caller waits until a failing owner publishes "
          "completion",
          "[application][coordinator][failure]") {
    using namespace std::chrono_literals;

    auto fixture = std::make_shared<Fixture>();
    auto closeEntered = std::make_shared<std::binary_semaphore>(0);
    auto releaseClose = std::make_shared<std::binary_semaphore>(0);
    auto waiterCalling = std::make_shared<std::binary_semaphore>(0);
    fixture->transport.failureStage_ = ShutdownFailureStage::kCloseTransport;
    fixture->transport.closeEntered_ = closeEntered.get();
    fixture->transport.releaseClose_ = releaseClose.get();

    std::thread owner([fixture] { fixture->coordinator.Shutdown(); });
    closeEntered->acquire();

    auto waiter =
        std::async(std::launch::async, [fixture, waiterCalling, releaseClose] {
            (void)releaseClose;
            waiterCalling->release();
            fixture->coordinator.Shutdown();
        }).share();
    waiterCalling->acquire();

    CHECK(waiter.wait_for(50ms) == std::future_status::timeout);
    releaseClose->release();

    owner.join();
    REQUIRE(waiter.wait_for(5s) == std::future_status::ready);
    waiter.get();
    CHECK_FALSE(fixture->coordinator.IsAvailable());
}

TEST_CASE("RunContained after shutdown still contains the exception without "
          "hanging or crashing",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    REQUIRE(f.coordinator.IsStopping());

    bool result = f.coordinator.RunContained(
        [] { throw std::runtime_error("late failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("ResetAvailability after shutdown does not affect the "
          "already-completed shutdown state",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    f.coordinator.ResetAvailability();

    CHECK(f.coordinator.IsAvailable());
    CHECK(f.coordinator.IsStopping()); //  shutdown remains in effect regardless.
}

TEST_CASE("RunContained does not let an exception propagate past it",
          "[application][coordinator][failure]") {
    //  If this were not caught, the test itself would fail with an unhandled
    //  exception rather than reaching the checks below.
    Fixture f;
    f.coordinator.RunContained(
        [] { throw std::runtime_error("must not escape"); });
    SUCCEED(
        "RunContained returned normally instead of propagating the exception");
}
