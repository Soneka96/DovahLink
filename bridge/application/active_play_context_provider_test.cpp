#include "application/active_play_context_provider.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <memory>
#include <stdexcept>

using dovahlink::application::ActivePlayContextProvider;
using dovahlink::application::IActivePlayContextProvider;
using dovahlink::application::PlayContext;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockPlayContextLifecycle;
using testing::StrictMock;

TEST_CASE("ActivePlayContextProvider forwards the current context unchanged",
          "[application][active_play_context_provider]") {
    StrictMock<MockPlayContextLifecycle> owner;
    auto context = BuildPlayContext("ctx-1");
    EXPECT_CALL(owner, CurrentPlayContext()).WillOnce(testing::Return(context));

    ActivePlayContextProvider provider(owner);
    const IActivePlayContextProvider& providerContract = provider;

    CHECK(providerContract.CurrentPlayContext() == context);
}

TEST_CASE("ActivePlayContextProvider forwards a null current context",
          "[application][active_play_context_provider]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContext())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));

    ActivePlayContextProvider provider(owner);

    CHECK_FALSE(provider.CurrentPlayContext());
}

TEST_CASE("ActivePlayContextProvider forwards each read without caching",
          "[application][active_play_context_provider]") {
    StrictMock<MockPlayContextLifecycle> owner;
    auto first = BuildPlayContext("ctx-1");
    auto second = BuildPlayContext("ctx-2");
    EXPECT_CALL(owner, CurrentPlayContext())
        .WillOnce(testing::Return(first))
        .WillOnce(testing::Return(second));

    ActivePlayContextProvider provider(owner);

    CHECK(provider.CurrentPlayContext() == first);
    CHECK(provider.CurrentPlayContext() == second);
}

TEST_CASE("ActivePlayContextProvider propagates owner read failures",
          "[application][active_play_context_provider]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContext())
        .WillOnce(testing::Throw(std::runtime_error("read failed")));

    ActivePlayContextProvider provider(owner);

    CHECK_THROWS_AS(provider.CurrentPlayContext(), std::runtime_error);
}
