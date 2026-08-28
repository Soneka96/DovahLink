#include "application/cadence_tick_driver.hpp"

#include "application/application_test_support.hpp"
#include "application/cadence_scheduler.hpp"
#include "application/capture_work_item.hpp"
#include "application/task_marshaller.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <cstddef>
#include <future>
#include <mutex>
#include <string>
#include <vector>

using dovahlink::application::CadenceTickDriver;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::ICadenceScheduler;
using dovahlink::application::ITaskMarshaller;
using dovahlink::application::RateClass;
using dovahlink::application::test_support::MockCaptureDispatchWorker;
using testing::_;
using testing::Invoke;
using testing::StrictMock;
using namespace std::chrono_literals;

namespace {

///  Controllable, thread-safe fake reporting a fixed set of due keys on
///  every call and counting how many times it was asked.
class FakeCadenceScheduler final : public ICadenceScheduler {
  public:
    void RegisterSampled(std::string, RateClass,
                         std::chrono::steady_clock::duration) override {}

    std::vector<std::string>
    DueKeys(std::chrono::steady_clock::time_point) override {
        std::lock_guard<std::mutex> lock(mutex_);
        ++callCount_;
        return dueKeys_;
    }

    void SetDueKeys(std::vector<std::string> keys) {
        std::lock_guard<std::mutex> lock(mutex_);
        dueKeys_ = std::move(keys);
    }

    std::size_t CallCount() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return callCount_;
    }

  private:
    mutable std::mutex mutex_;
    std::vector<std::string> dueKeys_;
    std::size_t callCount_ = 0;
};

///  Controllable, thread-safe fake that runs a marshaled task synchronously
///  on the calling thread and signals the first run.
class FakeTaskMarshaller final : public ITaskMarshaller {
  public:
    void RunOnGameThread(std::function<void()> task) override {
        task();
        std::lock_guard<std::mutex> lock(mutex_);
        if (!firstRunSignaled_) {
            firstRunSignaled_ = true;
            firstRun_.set_value();
        }
    }

    void WaitForFirstRun() { firstRun_.get_future().wait(); }

  private:
    std::mutex mutex_;
    std::promise<void> firstRun_;
    bool firstRunSignaled_ = false;
};

} //  namespace

TEST_CASE("A tick with no due keys enqueues nothing",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Start();
    taskMarshaller.WaitForFirstRun();
    driver.Stop();
    driver.Join();

    CHECK(scheduler.CallCount() >= 1);
}

TEST_CASE("A due key is marshaled onto the game thread and enqueued to the "
          "worker",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    scheduler.SetDueKeys({"character_level"});
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    std::promise<void> enqueued;
    bool enqueuedSignaled = false;
    std::mutex signalMutex;
    EXPECT_CALL(worker, TryEnqueue(_))
        .WillRepeatedly(Invoke([&](CaptureWorkItem) {
            std::lock_guard<std::mutex> lock(signalMutex);
            if (!enqueuedSignaled) {
                enqueuedSignaled = true;
                enqueued.set_value();
            }
            return true;
        }));
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Start();
    enqueued.get_future().wait();
    driver.Stop();
    driver.Join();
}

TEST_CASE("Each due key across a tick with multiple due keys is enqueued",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    scheduler.SetDueKeys({"area_a", "area_b"});
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    std::mutex signalMutex;
    std::vector<std::string> enqueuedAreas;
    std::promise<void> bothEnqueued;
    bool bothSignaled = false;
    EXPECT_CALL(worker, TryEnqueue(_))
        .WillRepeatedly(Invoke([&](CaptureWorkItem item) {
            std::lock_guard<std::mutex> lock(signalMutex);
            enqueuedAreas.push_back(item.stateArea);
            if (!bothSignaled && enqueuedAreas.size() >= 2) {
                bothSignaled = true;
                bothEnqueued.set_value();
            }
            return true;
        }));
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Start();
    bothEnqueued.get_future().wait();
    driver.Stop();
    driver.Join();

    std::lock_guard<std::mutex> lock(signalMutex);
    CHECK(enqueuedAreas.size() >= 2);
}

TEST_CASE("Join before Start is a safe no-op",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Join();
}

TEST_CASE("A second Stop call is a safe no-op",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);
    driver.Start();
    taskMarshaller.WaitForFirstRun();
    driver.Stop();

    driver.Stop();

    driver.Join();
}

TEST_CASE("A second Join call after Stop is a safe no-op",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);
    driver.Start();
    taskMarshaller.WaitForFirstRun();
    driver.Stop();
    driver.Join();

    driver.Join();
}

TEST_CASE("Stop before Start is a safe no-op",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Stop();
    driver.Join();
}

TEST_CASE("Stop before Start does not prevent a later tick",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    FakeTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 5ms);

    driver.Stop();
    driver.Start();
    taskMarshaller.WaitForFirstRun();
    driver.Stop();
    driver.Join();

    CHECK(scheduler.CallCount() >= 1);
}
