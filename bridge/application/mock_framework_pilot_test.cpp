#include "application/active_session_disconnector.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <string_view>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::test_support::MockActiveSessionDisconnector;
using testing::StrictMock;

TEST_CASE("GoogleMock verifies string-view arguments on an existing Bridge port",
          "[application][mock_framework]") {
    StrictMock<MockActiveSessionDisconnector> disconnector;
    EXPECT_CALL(disconnector,
                DisconnectIfClientActive("client-1", "revoked"));

    disconnector.DisconnectIfClientActive("client-1", "revoked");
}
