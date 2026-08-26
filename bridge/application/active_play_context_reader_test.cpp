#include "application/active_play_context_reader.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <memory>
#include <optional>
#include <stdexcept>
#include <string>

using dovahlink::application::ActivePlayContextReader;
using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::IPlayContextLifecycle;
using dovahlink::application::LifecycleEvent;
using dovahlink::application::LifecycleState;
using dovahlink::application::PlayContext;
using testing::StrictMock;

namespace {

///  GoogleMock lifecycle aggregate double for the read-only adapter.
class MockPlayContextLifecycle : public IPlayContextLifecycle {
  public:
    MOCK_METHOD(IPlayContextLifecycle::Transition, HandleEvent,
                (LifecycleEvent), (override));
    MOCK_METHOD(std::shared_ptr<PlayContext>, AcquireCurrent, (),
                (const, override));
    MOCK_METHOD(std::optional<std::string>, CurrentPlayContextId, (),
                (const, override));
    MOCK_METHOD(LifecycleState, CurrentState, (), (const, override));
};

} //  namespace

TEST_CASE("ActivePlayContextReader forwards the active context unchanged",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    auto context = std::make_shared<PlayContext>("ctx-1");
    EXPECT_CALL(owner, AcquireCurrent()).WillOnce(testing::Return(context));

    ActivePlayContextReader reader(owner);
    const IActivePlayContextReader& readerContract = reader;

    CHECK(readerContract.AcquireCurrent() == context);
}

TEST_CASE("ActivePlayContextReader forwards an empty context unchanged",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, AcquireCurrent())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));

    ActivePlayContextReader reader(owner);
    const IActivePlayContextReader& readerContract = reader;

    CHECK_FALSE(readerContract.AcquireCurrent());
}

TEST_CASE("ActivePlayContextReader forwards each read without caching",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    auto first = std::make_shared<PlayContext>("ctx-1");
    auto second = std::make_shared<PlayContext>("ctx-2");
    EXPECT_CALL(owner, AcquireCurrent())
        .WillOnce(testing::Return(first))
        .WillOnce(testing::Return(second));

    ActivePlayContextReader reader(owner);

    CHECK(reader.AcquireCurrent() == first);
    CHECK(reader.AcquireCurrent() == second);
}

TEST_CASE("ActivePlayContextReader propagates owner read failures",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, AcquireCurrent())
        .WillOnce(testing::Throw(std::runtime_error("read failed")));

    ActivePlayContextReader reader(owner);

    CHECK_THROWS_AS(reader.AcquireCurrent(), std::runtime_error);
}
