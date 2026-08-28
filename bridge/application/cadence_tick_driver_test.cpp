#include "application/cadence_tick_driver.hpp"

#include "application/application_test_support.hpp"
#include "application/cadence_scheduler.hpp"
#include "application/capture_work_item.hpp"
#include "application/task_marshaller.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <functional>
#include <future>
#include <mutex>
#include <stdexcept>
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

///  Controllable asynchronous fake that retains queued tasks until the test
///  explicitly runs them.
class QueuedTaskMarshaller final : public ITaskMarshaller {
  public:
    void RunOnGameThread(std::function<void()> task) override {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            tasks_.push_back(std::move(task));
        }
        changed_.notify_all();
    }

    bool WaitForTaskCount(
        std::size_t expected,
        std::chrono::milliseconds timeout = 100ms) {
        std::unique_lock<std::mutex> lock(mutex_);
        return changed_.wait_for(lock, timeout,
                                 [this, expected] {
                                     return tasks_.size() >= expected;
                                 });
    }

    std::size_t PendingTaskCount() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return tasks_.size();
    }

    void RunNextTask() {
        std::function<void()> task;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (tasks_.empty()) {
                return;
            }
            task = std::move(tasks_.front());
            tasks_.pop_front();
        }
        task();
    }

  private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::deque<std::function<void()>> tasks_;
};

///  Scheduler fake that holds a game-thread tick in flight until the test
///  releases it.
class BlockingCadenceScheduler final : public ICadenceScheduler {
  public:
    BlockingCadenceScheduler()
        : releaseFuture_(release_.get_future().share()) {}

    void RegisterSampled(std::string, RateClass,
                         std::chrono::steady_clock::duration) override {}

    std::vector<std::string>
    DueKeys(std::chrono::steady_clock::time_point) override {
        entered_.set_value();
        releaseFuture_.wait();
        return {};
    }

    void WaitForDueKeys() { entered_.get_future().wait(); }

    void Release() { release_.set_value(); }

  private:
    std::promise<void> entered_;
    std::promise<void> release_;
    std::shared_future<void> releaseFuture_;
};

///  Fake that rejects the first marshal attempt and runs later tasks
///  synchronously.
class ThrowOnceTaskMarshaller final : public ITaskMarshaller {
  public:
    void RunOnGameThread(std::function<void()> task) override {
        if (calls_++ == 0) {
            throw std::runtime_error("configured marshal failure");
        }
        task();
        if (calls_ == 2) {
            secondRun_.set_value();
        }
    }

    void WaitForSecondRun() { secondRun_.get_future().wait(); }

  private:
    std::size_t calls_ = 0;
    std::promise<void> secondRun_;
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

TEST_CASE("CadenceTickDriver keeps at most one game-thread tick queued",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    QueuedTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 1ms);

    driver.Start();
    REQUIRE(taskMarshaller.WaitForTaskCount(1));
    CHECK_FALSE(taskMarshaller.WaitForTaskCount(2, 50ms));

    driver.Stop();
    driver.Join();
    CHECK(taskMarshaller.PendingTaskCount() == 1);
    taskMarshaller.RunNextTask();
    CHECK(scheduler.CallCount() == 0);
}

TEST_CASE("A completed game-thread tick allows the next tick to be queued",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    QueuedTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 1ms);

    driver.Start();
    REQUIRE(taskMarshaller.WaitForTaskCount(1));
    taskMarshaller.RunNextTask();
    CHECK(scheduler.CallCount() == 1);
    REQUIRE(taskMarshaller.WaitForTaskCount(1));

    driver.Stop();
    driver.Join();
    taskMarshaller.RunNextTask();
    CHECK(scheduler.CallCount() == 1);
}

TEST_CASE("Join waits for an executing game-thread tick",
          "[application][cadence_tick_driver]") {
    BlockingCadenceScheduler scheduler;
    QueuedTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 1ms);

    driver.Start();
    REQUIRE(taskMarshaller.WaitForTaskCount(1));
    std::thread callbackRunner([&taskMarshaller] {
        taskMarshaller.RunNextTask();
    });
    scheduler.WaitForDueKeys();

    std::promise<void> joined;
    std::shared_future<void> joinedFuture = joined.get_future().share();
    std::thread joiner([&driver, &joined] {
        driver.Stop();
        driver.Join();
        joined.set_value();
    });

    CHECK(joinedFuture.wait_for(50ms) == std::future_status::timeout);
    scheduler.Release();
    joinedFuture.wait();
    joiner.join();
    callbackRunner.join();
}

TEST_CASE("A queued game-thread tick is safe after driver destruction",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    QueuedTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;

    {
        CadenceTickDriver driver(scheduler, worker, taskMarshaller, 1ms);
        driver.Start();
        REQUIRE(taskMarshaller.WaitForTaskCount(1));
    }

    taskMarshaller.RunNextTask();
    CHECK(scheduler.CallCount() == 0);
}

TEST_CASE("A failed marshal attempt does not stop future cadence ticks",
          "[application][cadence_tick_driver]") {
    FakeCadenceScheduler scheduler;
    ThrowOnceTaskMarshaller taskMarshaller;
    StrictMock<MockCaptureDispatchWorker> worker;
    CadenceTickDriver driver(scheduler, worker, taskMarshaller, 1ms);

    driver.Start();
    taskMarshaller.WaitForSecondRun();
    driver.Stop();
    driver.Join();

    CHECK(scheduler.CallCount() >= 1);
}
