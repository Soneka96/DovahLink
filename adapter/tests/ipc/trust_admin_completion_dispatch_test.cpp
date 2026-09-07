#include "ipc/trust_admin_completion_dispatch.hpp"

#include "ipc/adapter_task_marshaller_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <stdexcept>
#include <vector>

using dovahlink::adapter::ipc::DispatchTrustAdminCompletion;
using dovahlink::adapter::ipc::TrustAdminRequestOutcome;
using dovahlink::adapter::ipc::TrustAdminRequestResult;
using dovahlink::adapter::ipc::test_support::FakeAdapterTaskMarshaller;

namespace {

///  Builds a representative completed `TrustAdminRequestResult`.
TrustAdminRequestResult BuildCompletedResult() {
  return TrustAdminRequestResult{TrustAdminRequestOutcome::kCompleted,
                                 std::optional<std::string>("ok")};
}

} //  namespace

TEST_CASE("DispatchTrustAdminCompletion defers onResult until the fake "
          "game-thread queue is drained",
          "[trust_admin_completion_dispatch]") {
  FakeAdapterTaskMarshaller marshaller;
  bool invoked = false;
  DispatchTrustAdminCompletion(
      marshaller, [&invoked](TrustAdminRequestResult) { invoked = true; },
      BuildCompletedResult(), [] { FAIL("onDispatchFailed must not run"); });

  REQUIRE_FALSE(invoked);
  REQUIRE(marshaller.PendingCount() == 1);

  marshaller.RunAllPending();
  REQUIRE(invoked);
}

TEST_CASE("DispatchTrustAdminCompletion delivers the exact result moved in",
          "[trust_admin_completion_dispatch]") {
  FakeAdapterTaskMarshaller marshaller;
  std::optional<TrustAdminRequestResult> delivered;
  DispatchTrustAdminCompletion(
      marshaller,
      [&delivered](TrustAdminRequestResult result) {
        delivered = std::move(result);
      },
      BuildCompletedResult(), [] { FAIL("onDispatchFailed must not run"); });

  marshaller.RunAllPending();
  REQUIRE(delivered.has_value());
  CHECK(delivered->outcome == TrustAdminRequestOutcome::kCompleted);
  CHECK(delivered->resultText == std::optional<std::string>("ok"));
}

TEST_CASE("DispatchTrustAdminCompletion contains an exception thrown by "
          "onResult",
          "[trust_admin_completion_dispatch]") {
  FakeAdapterTaskMarshaller marshaller;
  DispatchTrustAdminCompletion(
      marshaller,
      [](TrustAdminRequestResult) {
        throw std::runtime_error("onResult failed");
      },
      BuildCompletedResult(), [] { FAIL("onDispatchFailed must not run"); });

  REQUIRE_NOTHROW(marshaller.RunAllPending());
}

TEST_CASE("DispatchTrustAdminCompletion never invokes onResult on the "
          "calling thread when RunOnGameThread fails to enqueue",
          "[trust_admin_completion_dispatch]") {
  FakeAdapterTaskMarshaller marshaller;
  marshaller.ThrowOnNextSchedule();
  bool onResultInvoked = false;
  bool dispatchFailedInvoked = false;

  DispatchTrustAdminCompletion(
      marshaller,
      [&onResultInvoked](TrustAdminRequestResult) { onResultInvoked = true; },
      BuildCompletedResult(),
      [&dispatchFailedInvoked] { dispatchFailedInvoked = true; });

  CHECK_FALSE(onResultInvoked);
  CHECK(dispatchFailedInvoked);
  CHECK(marshaller.PendingCount() == 0);
}

TEST_CASE("DispatchTrustAdminCompletion contains an exception thrown by "
          "onDispatchFailed",
          "[trust_admin_completion_dispatch]") {
  FakeAdapterTaskMarshaller marshaller;
  marshaller.ThrowOnNextSchedule();

  REQUIRE_NOTHROW(DispatchTrustAdminCompletion(
      marshaller, [](TrustAdminRequestResult) {}, BuildCompletedResult(),
      [] { throw std::runtime_error("onDispatchFailed failed"); }));
}
