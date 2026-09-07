#include "runtime/game_thread_completion.hpp"

#include "ipc/adapter_task_marshaller_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <stdexcept>

using dovahlink::adapter::ipc::test_support::FakeAdapterTaskMarshaller;
using dovahlink::adapter::runtime::RunOnGameThreadOrReportFailure;

TEST_CASE("RunOnGameThreadOrReportFailure defers task until the fake "
          "game-thread queue is drained",
          "[runtime][game_thread_completion]") {
  FakeAdapterTaskMarshaller marshaller;
  bool invoked = false;
  RunOnGameThreadOrReportFailure(
      marshaller, [&invoked] { invoked = true; },
      [] { FAIL("onDispatchFailed must not run"); });

  REQUIRE_FALSE(invoked);
  REQUIRE(marshaller.PendingCount() == 1);

  marshaller.RunAllPending();
  REQUIRE(invoked);
}

TEST_CASE("RunOnGameThreadOrReportFailure contains an exception thrown by "
          "task",
          "[runtime][game_thread_completion]") {
  FakeAdapterTaskMarshaller marshaller;
  RunOnGameThreadOrReportFailure(
      marshaller, [] { throw std::runtime_error("task failed"); },
      [] { FAIL("onDispatchFailed must not run"); });

  REQUIRE_NOTHROW(marshaller.RunAllPending());
}

TEST_CASE("RunOnGameThreadOrReportFailure never runs task on the calling "
          "thread when RunOnGameThread fails to enqueue",
          "[runtime][game_thread_completion]") {
  FakeAdapterTaskMarshaller marshaller;
  marshaller.ThrowOnNextSchedule();
  bool taskInvoked = false;
  bool dispatchFailedInvoked = false;

  RunOnGameThreadOrReportFailure(
      marshaller, [&taskInvoked] { taskInvoked = true; },
      [&dispatchFailedInvoked] { dispatchFailedInvoked = true; });

  CHECK_FALSE(taskInvoked);
  CHECK(dispatchFailedInvoked);
  CHECK(marshaller.PendingCount() == 0);
}

TEST_CASE("RunOnGameThreadOrReportFailure contains an exception thrown by "
          "onDispatchFailed",
          "[runtime][game_thread_completion]") {
  FakeAdapterTaskMarshaller marshaller;
  marshaller.ThrowOnNextSchedule();

  REQUIRE_NOTHROW(RunOnGameThreadOrReportFailure(
      marshaller, [] {},
      [] { throw std::runtime_error("onDispatchFailed failed"); }));
}
