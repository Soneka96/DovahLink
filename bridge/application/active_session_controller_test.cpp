#include "application/active_session_controller.hpp"

#include "application/application_test_support.hpp"
#include "protocol/bounded_json.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <memory>
#include <optional>
#include <string>

using dovahlink::application::ActiveSessionController;
using dovahlink::application::ActiveSessionSocket;
using dovahlink::application::SessionAuthMethod;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;
using dovahlink::application::test_support::EmptyActivePlayContext;
using dovahlink::application::test_support::MockActivePlayContext;
using dovahlink::application::test_support::MockSocket;
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

TEST_CASE("ActiveSessionController calls ISocket::Shutdown, not "
          "ShutdownWithNotification, for a pre-auth socket",
          "[application][active_session_controller][i_socket]") {
    auto socket = std::make_shared<StrictMock<MockSocket>>();
    EXPECT_CALL(*socket, Shutdown());
    ActiveSessionSocket activeSessionSocket;
    activeSessionSocket.Publish(1, socket);
    SessionManager sessionManager;
    StrictMock<MockActivePlayContext> activePlayContext;
    EXPECT_CALL(activePlayContext, CurrentPlayContextId()).Times(0);
    ActiveSessionController controller(sessionManager, activeSessionSocket,
                                       activePlayContext, std::nullopt);

    controller.DisconnectActive("factory_reset");
}

TEST_CASE("ActiveSessionController calls ISocket::ShutdownWithNotification "
          "with a session_invalidated envelope for a socket with an active "
          "session",
          "[application][active_session_controller][i_socket]") {
    SessionManager sessionManager;
    auto lease = sessionManager.TryCreateSession(
        1, "session-1", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(lease.has_value());
    auto socket = std::make_shared<StrictMock<MockSocket>>();
    std::string notification;
    EXPECT_CALL(*socket, ShutdownWithNotification(testing::_))
        .WillOnce(testing::SaveArg<0>(&notification));
    ActiveSessionSocket activeSessionSocket;
    activeSessionSocket.Publish(1, socket);
    EmptyActivePlayContext activePlayContext;
    ActiveSessionController controller(sessionManager, activeSessionSocket,
                                       activePlayContext, std::nullopt);

    controller.DisconnectActive("factory_reset");

    auto parsed = dovahlink::protocol::ParseBoundedJson(notification);
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "session_invalidated");
    REQUIRE(envelope->sessionId.has_value());
    CHECK(*envelope->sessionId == "session-1");
    CHECK(envelope->payload.at("reason").as_string() == "factory_reset");
}
