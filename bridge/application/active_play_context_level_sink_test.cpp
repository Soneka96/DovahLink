#include "application/active_play_context_level_sink.hpp"

#include "application/application_test_support.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::CaptureMode;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::test_support::MockCaptureDispatchWorker;
using dovahlink::application::test_support::MockPlayContextLifecycle;
using dovahlink::application::test_support::MockRegisteredStateAreaPolicy;
using testing::StrictMock;

namespace {
//  A private fixture key, not `character_level`: this proves the
//  registered-area gate and worker handoff mechanism generically, without
//  coupling this test to Phase 4.3's real domain identity or wire contract.
const std::string kFixtureStateArea = "test_state_area";
} //  namespace

TEST_CASE("ActivePlayContextLevelSink forwards a captured level to the "
          "lifecycle regardless of registration",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{12}));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);
    IActivePlayContextLevelSink& sinkContract = sink;

    sinkContract.OnLevelCaptured(12);
}

TEST_CASE("ActivePlayContextLevelSink forwards an unavailable level to the "
          "lifecycle regardless of registration",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{}));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(std::nullopt);
}

TEST_CASE("ActivePlayContextLevelSink does not enqueue when the state area "
          "is not registered",
          "[application][active_play_context_level_sink]") {
    //  StrictMock<MockCaptureDispatchWorker> with no TryEnqueue expectation
    //  fails the test if it is called at all -- proving 4.2's zero-registered
    //  behavior holds even though a real captured value flows through.
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{15}));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(15);
}

TEST_CASE("ActivePlayContextLevelSink enqueues a capture work item carrying "
          "the value when the state area is registered",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{15}));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    auto before = std::chrono::system_clock::now();
    sink.OnLevelCaptured(15);
    auto after = std::chrono::system_clock::now();

    CHECK(capturedItem.stateArea == kFixtureStateArea);
    CHECK(capturedItem.mode == CaptureMode::kEvent);
    CHECK(capturedItem.occurredAt >= before);
    CHECK(capturedItem.occurredAt <= after);
    boost::json::object data = capturedItem.buildData();
    REQUIRE(data.contains("capturedValue"));
    CHECK(data.at("capturedValue").as_int64() == 15);
}

TEST_CASE("ActivePlayContextLevelSink enqueues no fabricated value when the "
          "captured level is unavailable but the state area is registered",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{}));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(std::nullopt);

    boost::json::object data = capturedItem.buildData();
    CHECK_FALSE(data.contains("capturedValue"));
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive forwards the "
          "lifecycle's active state",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kActive));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);
    IActivePlayContextLevelSink& sinkContract = sink;

    CHECK(sinkContract.IsCaptureActive());
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive reports inactive "
          "while loading",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kLoading));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    CHECK_FALSE(sink.IsCaptureActive());
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive reports inactive "
          "with no play context",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kNoContext));
    ActivePlayContextLevelSink sink(lifecycle, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    CHECK_FALSE(sink.IsCaptureActive());
}
