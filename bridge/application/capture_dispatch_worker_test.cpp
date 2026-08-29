#include "application/capture_dispatch_worker.hpp"

#include "application/application_test_support.hpp"
#include "application/constants.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>

#include <chrono>
#include <functional>
#include <future>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::CaptureDispatchWorker;
using dovahlink::application::CaptureMode;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::IActivePlayContextProvider;
using dovahlink::application::kMaxCaptureQueueItems;
using dovahlink::application::PlayContext;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockActivePlayContextProvider;
using dovahlink::application::test_support::MockCaptureQueueDiagnostics;
using dovahlink::application::test_support::MockStatePublisher;
using testing::_;
using testing::Invoke;
using testing::StrictMock;
using namespace std::chrono_literals;

namespace {

///  Reports the same context on every call. Used where the exact number of
///  provider reads is not itself the behavior under test; tests that do
///  care about call count and liveness use a strict mock instead.
class FixedActivePlayContextProvider final : public IActivePlayContextProvider {
  public:
    explicit FixedActivePlayContextProvider(std::shared_ptr<PlayContext> context)
        : context_(std::move(context)) {}

    [[nodiscard]] std::shared_ptr<PlayContext> CurrentPlayContext() const override {
        return context_;
    }

  private:
    std::shared_ptr<PlayContext> context_;
};

///  Builds a representative work item pinned to `context`, with a
///  deterministic, side-effect-free closure that always reports a change.
///  Dispatch-mediated tests only need the (stateArea, mode) pair to reach
///  the mocked publisher correctly; the closure's own apply/change-detection
///  behavior is proven separately by ActivePlayContextLevelSink's own tests.
CaptureWorkItem BuildWorkItem(std::shared_ptr<PlayContext> context,
                              std::string stateArea, CaptureMode mode) {
    return CaptureWorkItem{
        .playContext = std::move(context),
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
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
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

    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "character_level", CaptureMode::kSnapshot)));

    dispatched.get_future().wait();
    worker.Stop();
    worker.Join();
}

TEST_CASE("An Event item is dispatched to PublishCapture with kEvent mode "
          "on the worker thread",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
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

    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "character_level", CaptureMode::kEvent)));

    dispatched.get_future().wait();
    worker.Stop();
    worker.Join();
}

TEST_CASE("Queued items are dispatched in FIFO order",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
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
    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "area_a", CaptureMode::kSnapshot)));
    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "area_b", CaptureMode::kSnapshot)));

    worker.Start();

    secondDispatched.get_future().wait();
    worker.Stop();
    worker.Join();
    std::lock_guard<std::mutex> lock(orderMutex);
    CHECK(dispatchOrder == std::vector<std::string>{"area_a", "area_b"});
}

TEST_CASE("Dispatch discards an item whose pinned context is already stale "
          "before the capture closure runs",
          "[application][capture_dispatch_worker]") {
    //  StrictMock<MockStatePublisher> with no expectation fails the test if
    //  PublishCapture is reached at all.
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    auto pinnedContext = BuildPlayContext("context-1");
    auto currentContext = BuildPlayContext("context-2");
    std::promise<void> staleChecked;
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::DoAll(
            testing::InvokeWithoutArgs([&] { staleChecked.set_value(); }),
            testing::Return(currentContext)));
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    bool closureRan = false;
    CaptureWorkItem item{
        .playContext = pinnedContext,
        .stateArea = "character_level",
        .mode = CaptureMode::kSnapshot,
        .applyAndBuildIfChanged =
            [&closureRan](PlayContext&) -> std::optional<boost::json::object> {
            closureRan = true;
            return boost::json::object{};
        },
        .occurredAt = std::chrono::system_clock::now(),
    };
    worker.Start();
    REQUIRE(worker.TryEnqueue(std::move(item)));

    staleChecked.get_future().wait();
    worker.Stop();
    worker.Join();

    CHECK_FALSE(closureRan);
}

TEST_CASE("Dispatch discards an item when the play context has since been "
          "invalidated entirely, not only replaced",
          "[application][capture_dispatch_worker]") {
    //  A revert to the main menu (no new context activated yet) reports
    //  nullptr, not a different context object; the staleness check must
    //  treat that as stale too, not just a pointer inequality against a
    //  non-null replacement.
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    auto pinnedContext = BuildPlayContext("context-1");
    std::promise<void> staleChecked;
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::DoAll(
            testing::InvokeWithoutArgs([&] { staleChecked.set_value(); }),
            testing::Return(std::shared_ptr<PlayContext>{})));
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    bool closureRan = false;
    CaptureWorkItem item{
        .playContext = pinnedContext,
        .stateArea = "character_level",
        .mode = CaptureMode::kSnapshot,
        .applyAndBuildIfChanged =
            [&closureRan](PlayContext&) -> std::optional<boost::json::object> {
            closureRan = true;
            return boost::json::object{};
        },
        .occurredAt = std::chrono::system_clock::now(),
    };
    worker.Start();
    REQUIRE(worker.TryEnqueue(std::move(item)));

    staleChecked.get_future().wait();
    worker.Stop();
    worker.Join();

    CHECK_FALSE(closureRan);
}

TEST_CASE("Dispatch does not publish when the capture closure reports no "
          "change",
          "[application][capture_dispatch_worker]") {
    //  StrictMock<MockStatePublisher> with no expectation fails the test if
    //  PublishCapture is reached at all.
    StrictMock<MockStatePublisher> publisher;
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    std::promise<void> closureRan;
    CaptureWorkItem item{
        .playContext = context,
        .stateArea = "character_level",
        .mode = CaptureMode::kSnapshot,
        .applyAndBuildIfChanged =
            [&closureRan](PlayContext&) -> std::optional<boost::json::object> {
            closureRan.set_value();
            return std::nullopt;
        },
        .occurredAt = std::chrono::system_clock::now(),
    };
    worker.Start();
    REQUIRE(worker.TryEnqueue(std::move(item)));

    closureRan.get_future().wait();
    worker.Stop();
    worker.Join();
}

TEST_CASE("Dispatch's stillCurrent predicate re-queries the active-context "
          "provider live rather than reusing its earlier answer",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    auto pinnedContext = BuildPlayContext("context-1");
    auto laterContext = BuildPlayContext("context-2");
    //  First call is Dispatch's own early staleness check (still pinned,
    //  proceeds). Second call is made from inside the stillCurrent predicate
    //  invoked by the publisher action below, and answers differently --
    //  proving the predicate queries the provider again rather than
    //  capturing the first call's result.
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(pinnedContext))
        .WillOnce(testing::Return(laterContext));
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    std::promise<bool> stillCurrentResult;
    EXPECT_CALL(publisher, PublishCapture(_, _, _, _, _, _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>& stillCurrent) {
            stillCurrentResult.set_value(stillCurrent());
            return true;
        }));
    worker.Start();

    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(pinnedContext, "character_level", CaptureMode::kSnapshot)));

    CHECK_FALSE(stillCurrentResult.get_future().get());
    worker.Stop();
    worker.Join();
}

TEST_CASE("Dispatch applies a captured value only to its own pinned "
          "context, never an unrelated one",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    auto contextA = BuildPlayContext("context-a");
    auto contextB = BuildPlayContext("context-b");
    FixedActivePlayContextProvider activeContext(contextA);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    std::promise<void> dispatched;
    EXPECT_CALL(publisher, PublishCapture(_, _, _, _, _, _, _))
        .WillOnce(Invoke([&](const std::string&, const std::string&, auto&,
                             CaptureMode, boost::json::object,
                             std::chrono::system_clock::time_point,
                             const std::function<bool()>&) {
            dispatched.set_value();
            return true;
        }));
    worker.Start();
    CaptureWorkItem item{
        .playContext = contextA,
        .stateArea = "character_level",
        .mode = CaptureMode::kEvent,
        .applyAndBuildIfChanged =
            [](PlayContext& context) -> std::optional<boost::json::object> {
            context.characterState.OnLevelCaptured(42);
            boost::json::object data;
            data["capturedValue"] = 42;
            return data;
        },
        .occurredAt = std::chrono::system_clock::now(),
    };

    REQUIRE(worker.TryEnqueue(std::move(item)));

    dispatched.get_future().wait();
    worker.Stop();
    worker.Join();

    CHECK(contextA->characterState.CurrentCharacterSnapshot().level == 42);
    CHECK_FALSE(contextB->characterState.CurrentCharacterSnapshot().level);
}

TEST_CASE("A second Join call after the worker has already stopped is a "
          "safe no-op",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    worker.Start();
    worker.Stop();
    worker.Join();

    worker.Join();
}

TEST_CASE("TryEnqueue rejects a new item once the queue is at capacity and "
          "reports the rejected item's state area and mode",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    //  The worker is never started, so nothing drains the queue.
    auto context = BuildPlayContext();
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(BuildWorkItem(
            context, "area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }
    EXPECT_CALL(diagnostics,
                RecordCaptureRejected("one_too_many", CaptureMode::kSnapshot));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem(context, "one_too_many", CaptureMode::kSnapshot)));
}

TEST_CASE("TryEnqueue reports a capacity rejection for an Event-mode item "
          "with its own mode",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    auto context = BuildPlayContext();
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(BuildWorkItem(
            context, "area_" + std::to_string(i), CaptureMode::kSnapshot)));
    }
    EXPECT_CALL(diagnostics,
                RecordCaptureRejected("character_level", CaptureMode::kEvent));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem(context, "character_level", CaptureMode::kEvent)));
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
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
    auto context = BuildPlayContext();
    for (std::size_t i = 0; i < kMaxCaptureQueueItems; ++i) {
        REQUIRE(worker.TryEnqueue(BuildWorkItem(
            context, "area_" + std::to_string(i), CaptureMode::kSnapshot)));
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
                    BuildWorkItem(context, "reentrant", CaptureMode::kSnapshot));
            }
        }));

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem(context, "one_too_many", CaptureMode::kSnapshot)));
    CHECK(reentrantRejected);
}

TEST_CASE("TryEnqueue rejects a new item after Stop without reporting a "
          "diagnostic",
          "[application][capture_dispatch_worker]") {
    //  StrictMock<MockCaptureQueueDiagnostics> with no expectation fails the
    //  test if it is called at all -- a post-Stop rejection is an expected
    //  shutdown outcome, not a capacity loss worth flagging.
    StrictMock<MockStatePublisher> publisher;
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);

    worker.Stop();

    CHECK_FALSE(worker.TryEnqueue(
        BuildWorkItem(BuildPlayContext(), "character_level", CaptureMode::kSnapshot)));
}

TEST_CASE("Stop discards a queued item that has not yet been dispatched",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
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

    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "area_a", CaptureMode::kSnapshot)));
    firstEntered.get_future().wait();
    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "area_b", CaptureMode::kSnapshot)));

    worker.Stop();
    releaseFirst.set_value();
    worker.Join();
}

TEST_CASE("Join waits for an in-flight dispatch to finish",
          "[application][capture_dispatch_worker]") {
    StrictMock<MockStatePublisher> publisher;
    auto context = BuildPlayContext();
    FixedActivePlayContextProvider activeContext(context);
    StrictMock<MockCaptureQueueDiagnostics> diagnostics;
    CaptureDispatchWorker worker(publisher, activeContext, diagnostics);
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
    REQUIRE(worker.TryEnqueue(
        BuildWorkItem(context, "character_level", CaptureMode::kSnapshot)));
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
