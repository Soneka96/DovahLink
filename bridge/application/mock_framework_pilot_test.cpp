#include "application/active_session_disconnector.hpp"

#include <catch2/catch_test_macros.hpp>
#include <fakeit.hpp>

#include <string_view>

using dovahlink::application::ActiveSessionDisconnector;
using fakeit::Mock;
using fakeit::Verify;
using fakeit::VerifyNoOtherInvocations;
using fakeit::When;

TEST_CASE("FakeIt verifies string-view arguments on an existing Bridge port",
          "[application][mock_framework]") {
    Mock<ActiveSessionDisconnector> disconnector;
    When(Method(disconnector, DisconnectIfClientActive)).Return();

    disconnector.get().DisconnectIfClientActive("client-1", "revoked");

    Verify(Method(disconnector, DisconnectIfClientActive).Using(
               std::string_view{"client-1"}, std::string_view{"revoked"}))
        .Once();
    VerifyNoOtherInvocations(disconnector);
}
