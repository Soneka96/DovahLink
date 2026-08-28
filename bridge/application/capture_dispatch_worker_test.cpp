#include "application/capture_dispatch_worker.hpp"

#include "application/application_test_support.hpp"
#include "application/constants.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>

#include <chrono>
#include <future>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::CaptureDispatchWorker;
using dovahlink::application::CaptureMode;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::kMaxCaptureQueueItems;
using dovahlink::application::test_support::MockStatePublisher;
using testing::_;
using testing::Invoke;
using testing::StrictMock;
using namespace std::chrono_literals;

namespace {

///  Builds a representative work item with a deterministic, side-effect-free
///  payload builder.
CaptureWorkItem BuildWorkItem(std::string stateArea, CaptureMode mode) {
    return CaptureWorkItem{
        .stateArea = std::move(stateArea),
        .mode = mode,
        .buildData = [] { return boost::json::object{}; },
        .occurredAt = std::chrono::system_clock::now(),
    };
}

} //  namespace

TEST_CASE("A Snapshot item is dispatched to PublishSnapshot on the worker "
          "thread",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    std::promise<void> dispatched;
    EXPECT_CALL(publisher, PublishSnapshot("character_level", _, _))
        .WillOnce(Invoke([&](const std::string&, boost::json::object,
                             std::chrono::system_clock::time_point) {
            dispatched.set_value();
            return true;
        }));
    worker.Start();

    REQUIRE(
        worker.TryEnqueue(BuildWorkItem("character_level", CaptureMode::kSnapshot)));

    dispatched.get_future().wait();
    worker.Stop();
    worker.Join();
}

TEST_CASE("An Event item is dispatched to PublishEvent on the worker thread",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    std::promise<void> dispatched;
    EXPECT_CALL(publisher, PublishEvent("character_level", _, _))
        .WillOnce(Invoke([&](const std::string&, boost::json::object,
                             std::chrono::system_clock::time_point) {
            dispatched.set_value();
            return true;
        }));
    worker.Start();

    REQUIRE(
        worker.TryEnqueue(BuildWorkItem("character_level", CaptureMode::kEvent)));

    dispatched.get_future().wait();
    worker.Stop();
    worker.Join();
}

TEST_CASE("Queued items are dispatched in FIFO order",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    std::mutex orderMutex;
    std::vector<std::string> dispatchOrder;
    std::promise<void> secondDispatched;
    EXPECT_CALL(publisher, PublishSnapshot(_, _, _))
        .WillRepeatedly(
            Invoke([&](const std::string& stateArea, boost::json::object,
                       std::chrono::system_clock::time_point) {
                std::lock_guard<std::mutex> lock(orderMutex);
                dispatchOrder.push_back(stateArea);
                if (dispatchOrder.size() == 2) {
                    secondDispatched.set_value();
                }
                return true;
            }));
    //  Enqueued before Start, so both are queued before the worker thread
    //  can dequeue either one.
    REQUIRE(worker.TryEnqueue(BuildWorkItem("area_a", CaptureMode::kSnapshot)));
    REQUIRE(worker.TryEnqueue(BuildWorkItem("area_b", CaptureMode::kSnapshot)));

    worker.Start();

    secondDispatched.get_future().wait();
    worker.Stop();
    worker.Join();
    std::lock_guard<std::mutex> lock(orderMutex);
    CHECK(dispatchOrder == std::vector<std::string>{"area_a", "area_b"});
}

TEST_CASE("A second Join call after the worker has already stopped is a "
          "safe no-op",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    worker.Start();
    worker.Stop();
    worker.Join();

    worker.Join();
}

TEST_CASE("TryEnqueue rejects a new item once the queue is at capacity",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    //  The worker is never started, so nothing drains the queue.
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(
            BuildWorkItem("area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("one_too_many", CaptureMode::kSnapshot)));
}

TEST_CASE("TryEnqueue rejects a new item after Stop",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);

    worker.Stop();

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("character_level", CaptureMode::kSnapshot)));
}

TEST_CASE("Stop discards a queued item that has not yet been dispatched",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    std::promise<void> firstEntered;
    std::promise<void> releaseFirst;
    std::shared_future<void> releaseFirstFuture = releaseFirst.get_future();
    //  Only "area_a" is ever expected; a call for "area_b" would fail this
    //  strict mock, proving it was discarded rather than dispatched.
    EXPECT_CALL(publisher, PublishSnapshot("area_a", _, _))
        .WillOnce(Invoke([&](const std::string&, boost::json::object,
                             std::chrono::system_clock::time_point) {
            firstEntered.set_value();
            releaseFirstFuture.wait();
            return true;
        }));
    worker.Start();

    REQUIRE(worker.TryEnqueue(BuildWorkItem("area_a", CaptureMode::kSnapshot)));
    firstEntered.get_future().wait();
    REQUIRE(worker.TryEnqueue(BuildWorkItem("area_b", CaptureMode::kSnapshot)));

    worker.Stop();
    releaseFirst.set_value();
    worker.Join();
}

TEST_CASE("Join waits for an in-flight dispatch to finish",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    CaptureDispatchWorker worker(publisher);
    std::promise<void> entered;
    std::promise<void> release;
    std::shared_future<void> releaseFuture = release.get_future();
    EXPECT_CALL(publisher, PublishSnapshot("character_level", _, _))
        .WillOnce(Invoke([&](const std::string&, boost::json::object,
                             std::chrono::system_clock::time_point) {
            entered.set_value();
            releaseFuture.wait();
            return true;
        }));
    worker.Start();
    REQUIRE(
        worker.TryEnqueue(BuildWorkItem("character_level", CaptureMode::kSnapshot)));
    entered.get_future().wait();
    worker.Stop();

    std::promise<void> joined;
    std::thread joiner([&] {
        worker.Join();
        joined.set_value();
    });

    CHECK(joined.get_future().wait_for(100ms) == std::future_status::timeout);

    release.set_value();
    joined.get_future().wait();
    joiner.join();
}
