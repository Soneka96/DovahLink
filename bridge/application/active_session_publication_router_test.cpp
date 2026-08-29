#include "application/active_session_publication_router.hpp"

#include "application/application_test_support.hpp"
#include "protocol/envelope.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <string>
#include <utility>

using dovahlink::application::ActiveSessionPublicationRouter;
using dovahlink::application::test_support::MockOutboundPublicationSink;
using dovahlink::protocol::Envelope;
using testing::_;
using testing::StrictMock;

namespace {

///  Builds a representative envelope; its exact contents are irrelevant to
///  these routing tests.
Envelope BuildTestEnvelope(std::string messageId = "msg-1") {
    return Envelope{.messageType = "state_snapshot",
                    .messageId = std::move(messageId)};
}

} //  namespace

TEST_CASE("PublishSnapshot is a no-op when no sink is attached",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;

    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("PublishEvent is a no-op when no sink is attached",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;

    router.PublishEvent("character_level", BuildTestEnvelope());
}

TEST_CASE("PublishRecoverySnapshot is a no-op when no sink is attached",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;

    router.PublishRecoverySnapshot("character_level", BuildTestEnvelope(), 7);
}

TEST_CASE("PublishControl is a no-op when no sink is attached",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;

    router.PublishControl(BuildTestEnvelope());
}

TEST_CASE("PublishSnapshot forwards to the attached sink",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);
    EXPECT_CALL(sink, PublishSnapshot("character_level", _)).Times(1);

    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("PublishEvent forwards to the attached sink",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);
    EXPECT_CALL(sink, PublishEvent("character_level", _)).Times(1);

    router.PublishEvent("character_level", BuildTestEnvelope());
}

TEST_CASE("PublishRecoverySnapshot forwards to the attached sink with its "
          "revision",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);
    EXPECT_CALL(sink,
                PublishRecoverySnapshot("character_level", _, std::int64_t{7}))
        .Times(1);

    router.PublishRecoverySnapshot("character_level", BuildTestEnvelope(), 7);
}

TEST_CASE("PublishControl forwards to the attached sink",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);
    EXPECT_CALL(sink, PublishControl(_)).Times(1);

    router.PublishControl(BuildTestEnvelope());
}

TEST_CASE("Detach with nothing attached is a safe no-op",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;

    router.Detach();

    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("Detach stops forwarding to the previously attached sink",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);
    router.Detach();

    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("Attach replaces a previously attached sink",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> firstSink;
    StrictMock<MockOutboundPublicationSink> secondSink;
    router.Attach(firstSink);
    router.Attach(secondSink);
    EXPECT_CALL(secondSink, PublishSnapshot("character_level", _)).Times(1);

    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("expected detach does not remove a replacement binding",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> firstSink;
    StrictMock<MockOutboundPublicationSink> secondSink;
    router.Attach(firstSink);
    router.Attach(secondSink);
    EXPECT_CALL(secondSink, PublishSnapshot("character_level", _)).Times(1);

    router.Detach(firstSink);
    router.PublishSnapshot("character_level", BuildTestEnvelope());
}

TEST_CASE("expected detach clears its matching binding",
          "[application][active_session_publication_router]") {
    ActiveSessionPublicationRouter router;
    StrictMock<MockOutboundPublicationSink> sink;
    router.Attach(sink);

    router.Detach(sink);
    router.PublishSnapshot("character_level", BuildTestEnvelope());
}
