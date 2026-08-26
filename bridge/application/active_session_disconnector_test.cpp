#include "application/active_session_disconnector.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::IActiveSessionDisconnector;
using dovahlink::application::test_support::MockActiveSessionController;
using testing::StrictMock;

TEST_CASE("ActiveSessionDisconnector forwards targeted invalidation",
          "[application][active_session_disconnector]") {
    StrictMock<MockActiveSessionController> controller;
    EXPECT_CALL(controller,
                DisconnectIfClientActive("client-1", "revoked"));

    ActiveSessionDisconnector disconnector(controller);
    IActiveSessionDisconnector& contract = disconnector;
    contract.DisconnectIfClientActive("client-1", "revoked");
}

TEST_CASE("ActiveSessionDisconnector forwards unconditional invalidation",
          "[application][active_session_disconnector]") {
    StrictMock<MockActiveSessionController> controller;
    EXPECT_CALL(controller, DisconnectActive("factory_reset"));

    ActiveSessionDisconnector disconnector(controller);
    IActiveSessionDisconnector& contract = disconnector;
    contract.DisconnectActive("factory_reset");
}
