#include "application/capture_dispatch_worker.hpp"

#include "application/application_test_support.hpp"
#include "application/constants.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>

#include <chrono>
#include <functional>
#include <future>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::CaptureDispatchWorker;
using dovahlink::application::CaptureMode;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::kMaxCaptureQueueItems;
using dovahlink::application::PlayContext;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockCaptureQueueDiagnostics;
using dovahlink::application::test_support::MockStatePublisher;
using testing::_;
using testing::Invoke;
using testing::StrictMock;
using namespace std::chrono_literals;

namespace {

///  Builds a representative work item pinned to a fresh play context, with a
///  deterministic, side-effect-free closure that always reports a change.
///  Dispatch-mediated tests only need the (stateArea, mode) pair to reach
///  the mocked publisher correctly; the closure's own apply/change-detection
///  behavior is proven separately by ActivePlayContextLevelSink's own tests.
CaptureWorkItem BuildWorkItem(std::string stateArea, CaptureMode mode) {
    return CaptureWorkItem{
        .playContext = BuildPlayContext(),
        .stateArea = std::move(stateArea),
        .mode = mode,
        .applyAndBuildIfChanged =
            [](PlayContext&) -> std::optional<boost::json::object> {
            return boost::json::object{};
        },
        .occurredAt = std::chrono::system_clock::now(),
    };
}

} //  namespace

TEST_CASE("A Snapshot item is dispatched to PublishCapture with kSnapshot "
          "mode on the worker thread",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    std::promise<void> dispatched;
    EXPECT_CALL(publisher,
                PublishCapture("character_level", _, _, CaptureMode::kSnapshot,
                               _, _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>&) {
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

TEST_CASE("An Event item is dispatched to PublishCapture with kEvent mode "
          "on the worker thread",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    std::promise<void> dispatched;
    EXPECT_CALL(publisher,
                PublishCapture("character_level", _, _, CaptureMode::kEvent, _,
                               _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>&) {
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
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    std::mutex orderMutex;
    std::vector<std::string> dispatchOrder;
    std::promise<void> secondDispatched;
    EXPECT_CALL(publisher, PublishCapture(_, _, _, _, _, _, _))
        .WillRepeatedly(
            Invoke([&](const std::string& stateArea, const std::string&, auto&,
                       CaptureMode, boost::json::object,
                       std::chrono::system_clock::time_point,
                       const std::function<bool()>&) {
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
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    worker.Start();
    worker.Stop();
    worker.Join();

    worker.Join();
}

TEST_CASE("TryEnqueue rejects a new item once the queue is at capacity and "
          "reports the rejected item's state area and mode",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    //  The worker is never started, so nothing drains the queue.
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(
            BuildWorkItem("area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }
    EXPECT_CALL(diagnostics,
                RecordCaptureRejected("one_too_many", CaptureMode::kSnapshot));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("one_too_many", CaptureMode::kSnapshot)));
}

TEST_CASE("TryEnqueue reports a capacity rejection for an Event-mode item "
          "with its own mode",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(
            BuildWorkItem("area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }
    EXPECT_CALL(diagnostics,
                RecordCaptureRejected("character_level", CaptureMode::kEvent));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("character_level", CaptureMode::kEvent)));
}

TEST_CASE("TryEnqueue's diagnostics report is not made while the internal "
          "queue mutex is held",
          "[application][capture_dispatch_worker]") {
    //  A reentrant TryEnqueue call issued from inside the diagnostics
    //  callback would deadlock on the same non-recursive mutex if
    //  RecordCaptureRejected ran while it was still held; succeeding here
    //  proves the report happens outside the lock deterministically,
    //  without needing a genuine multi-threaded race.
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(
            BuildWorkItem("area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }
    //  The reentrant TryEnqueue below is itself rejected for capacity (the
    //  queue is still full), which reports a second, nested
    //  RecordCaptureRejected call -- accounted for by Times(2) rather than
    //  a mismatched strict expectation. The reentered guard keeps only the
    //  first (outer) call issuing the reentrant probe.
    bool reentered = false;
    bool reentrantRejected = false;
    EXPECT_CALL(diagnostics, RecordCaptureRejected(_, _))
        .Times(2)
        .WillRepeatedly(Invoke([&](std::string_view, CaptureMode) {
            if (!reentered) {
                reentered = true;
                reentrantRejected = !worker.TryEnqueue(
                    BuildWorkItem("reentrant", CaptureMode::kSnapshot));
            }
        }));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("one_too_many", CaptureMode::kSnapshot)));
    CHECK(reentrantRejected);
}

TEST_CASE("TryEnqueue rejects a new item after Stop without reporting a "
          "diagnostic",
          "[application][capture_dispatch_worker]") {
    //  StrictMock<MockCaptureQueueDiagnostics> with no expectation fails the
    //  test if it is called at all -- a post-Stop rejection is an expected
    //  shutdown outcome, not a capacity loss worth flagging.
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);

    worker.Stop();

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem("character_level", CaptureMode::kSnapshot)));
}

TEST_CASE("Stop discards a queued item that has not yet been dispatched",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    std::promise<void> firstEntered;
    std::promise<void> releaseFirst;
    std::shared_future<void> releaseFirstFuture = releaseFirst.get_future();
    //  Only "area_a" is ever expected; a call for "area_b" would fail this
    //  strict mock, proving it was discarded rather than dispatched.
    EXPECT_CALL(publisher, PublishCapture("area_a", _, _, _, _, _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>&) {
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
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, diagnostics);
    std::promise<void> entered;
    std::promise<void> release;
    std::shared_future<void> releaseFuture = release.get_future();
    EXPECT_CALL(publisher, PublishCapture("character_level", _, _, _, _, _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>&) {
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
    std::shared_future<void> joinedFuture = joined.get_future();
    std::thread joiner([&] {
        worker.Join();
        joined.set_value();
    });

    CHECK(joinedFuture.wait_for(100ms) == std::future_status::timeout);

    release.set_value();
    joinedFuture.wait();
    joiner.join();
}
