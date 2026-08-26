#include "application/active_play_context_reader.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>

using dovahlink::application::ActivePlayContextReader;
using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::test_support::MockPlayContextLifecycle;
using testing::StrictMock;

TEST_CASE("ActivePlayContextReader forwards the current ID unchanged",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContextId())
        .WillOnce(testing::Return(std::optional<std::string>{"ctx-1"}));

    ActivePlayContextReader reader(owner);
    const IActivePlayContextReader& readerContract = reader;

    CHECK(readerContract.CurrentPlayContextId() ==
          std::optional<std::string>{"ctx-1"});
}

TEST_CASE("ActivePlayContextReader forwards an empty current ID",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContextId())
        .WillOnce(testing::Return(std::optional<std::string>{}));

    ActivePlayContextReader reader(owner);

    CHECK_FALSE(reader.CurrentPlayContextId());
}

TEST_CASE("ActivePlayContextReader forwards each ID read without caching",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContextId())
        .WillOnce(testing::Return(std::optional<std::string>{"ctx-1"}))
        .WillOnce(testing::Return(std::optional<std::string>{"ctx-2"}));

    ActivePlayContextReader reader(owner);

    CHECK(reader.CurrentPlayContextId() ==
          std::optional<std::string>{"ctx-1"});
    CHECK(reader.CurrentPlayContextId() ==
          std::optional<std::string>{"ctx-2"});
}

TEST_CASE("ActivePlayContextReader propagates owner ID read failures",
          "[application][active_play_context_reader]") {
    StrictMock<MockPlayContextLifecycle> owner;
    EXPECT_CALL(owner, CurrentPlayContextId())
        .WillOnce(testing::Throw(std::runtime_error("read failed")));

    ActivePlayContextReader reader(owner);

    CHECK_THROWS_AS(reader.CurrentPlayContextId(), std::runtime_error);
}
