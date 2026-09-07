#include "ipc/trust_admin_request_result.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::adapter::ipc::TrustAdminRequestOutcome;
using dovahlink::adapter::ipc::TrustAdminRequestResult;

TEST_CASE("TrustAdminRequestResult defaults outcome to kUnavailable when not "
          "explicitly initialized",
          "[trust_admin_request_result]") {
  TrustAdminRequestResult result;

  REQUIRE(result.outcome == TrustAdminRequestOutcome::kUnavailable);
  REQUIRE_FALSE(result.resultText.has_value());
}
