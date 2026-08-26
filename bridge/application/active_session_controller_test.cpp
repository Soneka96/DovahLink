#include "application/active_session_controller.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <optional>

using dovahlink::application::ActiveSessionController;
using dovahlink::application::ActiveSessionSocket;
using dovahlink::application::SessionManager;
using dovahlink::application::test_support::MockActivePlayContext;
using dovahlink::transport::WebSocketSession;
using testing::StrictMock;

TEST_CASE("ActiveSessionController closes a pre-auth socket without notification",
          "[application][active_session_controller]") {
    boost::asio::io_context ioContext;
    auto socket = WebSocketSession::CreateSocket(
        boost::asio::ip::tcp::socket(ioContext));
    ActiveSessionSocket activeSessionSocket;
    activeSessionSocket.Publish(1, socket);
    SessionManager sessionManager;
    StrictMock<MockActivePlayContext> activePlayContext;
    EXPECT_CALL(activePlayContext, CurrentPlayContextId()).Times(0);
    ActiveSessionController controller(sessionManager, activeSessionSocket,
                                       activePlayContext, std::nullopt);

    controller.DisconnectActive("factory_reset");
}
