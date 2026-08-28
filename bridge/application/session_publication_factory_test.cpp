#include "application/session_publication_factory.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <memory>

using dovahlink::application::ActiveSessionPublicationRouter;
using dovahlink::application::BoundedOutboundQueue;
using dovahlink::application::SessionPublicationFactory;
using dovahlink::application::test_support::BuildEnvelope;
using dovahlink::application::test_support::MockPublicationDiagnostics;
using dovahlink::application::test_support::MockSocket;
using testing::_;
using testing::HasSubstr;
using testing::NiceMock;
using testing::StrictMock;

TEST_CASE("CreateForSession returns a non-null queue",
          "[application][session_publication_factory]") {
    ActiveSessionPublicationRouter router;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    NiceMock<MockSocket> socket;
    SessionPublicationFactory factory(router, diagnostics);

    std::unique_ptr<BoundedOutboundQueue> queue =
        factory.CreateForSession(socket, "session-1");

    CHECK(queue != nullptr);
}

TEST_CASE("CreateForSession attaches the created queue to the router, "
          "stamped with its sessionId",
          "[application][session_publication_factory]") {
    ActiveSessionPublicationRouter router;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    NiceMock<MockSocket> socket;
    EXPECT_CALL(socket, Send(HasSubstr("session-1"), _)).Times(1);
    SessionPublicationFactory factory(router, diagnostics);
    std::unique_ptr<BoundedOutboundQueue> queue =
        factory.CreateForSession(socket, "session-1");

    router.PublishControl(BuildEnvelope());
}

TEST_CASE("CreateForSession attaches correctly after an explicit Detach",
          "[application][session_publication_factory]") {
    ActiveSessionPublicationRouter router;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    NiceMock<MockSocket> firstSocket;
    NiceMock<MockSocket> secondSocket;
    EXPECT_CALL(secondSocket, Send(HasSubstr("session-2"), _)).Times(1);
    SessionPublicationFactory factory(router, diagnostics);
    std::unique_ptr<BoundedOutboundQueue> firstQueue =
        factory.CreateForSession(firstSocket, "session-1");
    router.Detach();

    std::unique_ptr<BoundedOutboundQueue> secondQueue =
        factory.CreateForSession(secondSocket, "session-2");
    router.PublishControl(BuildEnvelope());
}

TEST_CASE("A second CreateForSession call replaces the router's previous "
          "session binding",
          "[application][session_publication_factory]") {
    ActiveSessionPublicationRouter router;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    StrictMock<MockSocket> firstSocket;
    NiceMock<MockSocket> secondSocket;
    EXPECT_CALL(secondSocket, Send(HasSubstr("session-2"), _)).Times(1);
    SessionPublicationFactory factory(router, diagnostics);
    std::unique_ptr<BoundedOutboundQueue> firstQueue =
        factory.CreateForSession(firstSocket, "session-1");

    std::unique_ptr<BoundedOutboundQueue> secondQueue =
        factory.CreateForSession(secondSocket, "session-2");
    router.PublishControl(BuildEnvelope());
}
