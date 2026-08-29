#include "application/active_play_context_level_sink.hpp"

#include "application/application_test_support.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <cstdint>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::CaptureMode;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::PlayContext;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockActivePlayContextProvider;
using dovahlink::application::test_support::MockCaptureDispatchWorker;
using dovahlink::application::test_support::MockRegisteredStateAreaPolicy;
using testing::StrictMock;

namespace {
//  A private fixture key, not `character_level`: this proves the
//  registered-area gate and worker handoff mechanism generically, without
//  coupling this test to Phase 4.3's real domain identity or wire contract.
const std::string kFixtureStateArea = "test_state_area";
} //  namespace

TEST_CASE("ActivePlayContextLevelSink's BeginCapture returns nullptr when no "
          "context is active",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);
    IActivePlayContextLevelSink& sinkContract = sink;

    CHECK_FALSE(sinkContract.BeginCapture());
}

TEST_CASE("ActivePlayContextLevelSink's BeginCapture returns the active "
          "context",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(context));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    CHECK(sink.BeginCapture() == context);
}

TEST_CASE("ActivePlayContextLevelSink's OnLevelCaptured does nothing when "
          "given no pinned context",
          "[application][active_play_context_level_sink]") {
    //  StrictMock<MockRegisteredStateAreaPolicy>/MockCaptureDispatchWorker
    //  with no expectations fail the test if either is touched at all --
    //  proving a null capture is dropped before any registration check.
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(std::shared_ptr<PlayContext>{}, 12);
}

TEST_CASE("ActivePlayContextLevelSink's OnLevelCaptured does not enqueue "
          "when the state area is not registered",
          "[application][active_play_context_level_sink]") {
    //  StrictMock<MockCaptureDispatchWorker> with no TryEnqueue expectation
    //  fails the test if it is called at all.
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(context, 15);
}

TEST_CASE("ActivePlayContextLevelSink's OnLevelCaptured never re-queries the "
          "active-context provider",
          "[application][active_play_context_level_sink]") {
    //  EXPECT_CALL's default cardinality is exactly once: if OnLevelCaptured
    //  queried activeContext_ again internally instead of using the passed
    //  capture, this mock would receive a second call and fail -- proving
    //  BeginCapture is the sole source of the capture target, closing the
    //  race between the active-context guard and the runtime read it gates.
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(context));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    auto capture = sink.BeginCapture();
    sink.OnLevelCaptured(capture, 15);
}

TEST_CASE("ActivePlayContextLevelSink's OnLevelCaptured never re-queries the "
          "active-context provider when the state area is registered",
          "[application][active_play_context_level_sink]") {
    //  Same proof as the unregistered-path test above, extended to the
    //  enqueue path: EXPECT_CALL's default cardinality is exactly once, so a
    //  second internal CurrentPlayContext() call from the registered branch
    //  would also fail this test.
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(context));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::Return(true));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    auto capture = sink.BeginCapture();
    sink.OnLevelCaptured(capture, 15);
}

TEST_CASE("ActivePlayContextLevelSink's BeginCapture propagates provider "
          "read failures",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Throw(std::runtime_error("read failed")));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    CHECK_THROWS_AS(sink.BeginCapture(), std::runtime_error);
}

TEST_CASE("ActivePlayContextLevelSink's OnLevelCaptured enqueues a capture "
          "work item pinned to the given context when registered",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    auto before = std::chrono::system_clock::now();
    sink.OnLevelCaptured(context, 15);
    auto after = std::chrono::system_clock::now();

    CHECK(capturedItem.playContext == context);
    CHECK(capturedItem.stateArea == kFixtureStateArea);
    CHECK(capturedItem.mode == CaptureMode::kEvent);
    CHECK(capturedItem.occurredAt >= before);
    CHECK(capturedItem.occurredAt <= after);
    REQUIRE(capturedItem.applyAndBuildIfChanged);
    auto data = capturedItem.applyAndBuildIfChanged(*context);
    REQUIRE(data.has_value());
    REQUIRE(data->contains("capturedValue"));
    CHECK(data->at("capturedValue").as_int64() == 15);
    CHECK(context->characterState.CurrentCharacterSnapshot().level == 15);
}

TEST_CASE("ActivePlayContextLevelSink's captured closure omits capturedValue "
          "for an unavailable level",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(context, std::nullopt);

    auto data = capturedItem.applyAndBuildIfChanged(*context);
    REQUIRE(data.has_value());
    CHECK_FALSE(data->contains("capturedValue"));
}

TEST_CASE("ActivePlayContextLevelSink's captured closure reports no change "
          "for a repeated equal capture",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextProvider> activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    context->characterState.OnLevelCaptured(15);
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(kFixtureStateArea))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink sink(activeContext, registeredAreaPolicy,
                                    captureWorker, kFixtureStateArea);

    sink.OnLevelCaptured(context, 15);

    CHECK_FALSE(capturedItem.applyAndBuildIfChanged(*context).has_value());
}
