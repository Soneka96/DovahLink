#include "application/play_context_lifecycle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::DecodePostLoadGameSuccess;
using dovahlink::application::IPlayContextLifecycle;
using dovahlink::application::LifecycleEvent;
using dovahlink::application::LifecycleState;
using dovahlink::application::PlayContext;
using dovahlink::application::PlayContextFactory;
using dovahlink::application::PlayContextLifecycle;
using dovahlink::application::PlayContextLifecycleIdGenerator;

TEST_CASE("DecodePostLoadGameSuccess accepts only a non-null success payload",
          "[application][play_context_lifecycle]") {
    CHECK(DecodePostLoadGameSuccess(reinterpret_cast<const void*>(1)));
    CHECK_FALSE(DecodePostLoadGameSuccess(nullptr));
}

TEST_CASE("PlayContextLifecycle starts without an active context",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle;
    IPlayContextLifecycle& contract = lifecycle;

    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(contract.CurrentPlayContextId());
}

TEST_CASE("PlayContextLifecycle activates and replaces contexts atomically",
          "[application][play_context_lifecycle]") {
    int nextId = 1;
    PlayContextLifecycle lifecycle(
        [&nextId] { return std::optional<std::string>(
                        "context-" + std::to_string(nextId++)); });
    IPlayContextLifecycle& contract = lifecycle;

    auto first = contract.HandleEvent(LifecycleEvent::kNewGame);
    REQUIRE(first.newPlayContextId.has_value());
    CHECK(contract.CurrentPlayContextId() == first.newPlayContextId);
    CHECK(contract.CurrentState() == LifecycleState::kActive);

    auto replacement = contract.HandleEvent(LifecycleEvent::kNewGame);
    CHECK(replacement.contextInvalidated);
    REQUIRE(replacement.newPlayContextId.has_value());
    CHECK(contract.CurrentPlayContextId() == replacement.newPlayContextId);
    CHECK(contract.CurrentState() == LifecycleState::kActive);
}

TEST_CASE("PlayContextLifecycle invalidates state through load and revert events",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle([] {
        return std::optional<std::string>("context-1");
    });
    IPlayContextLifecycle& contract = lifecycle;
    contract.HandleEvent(LifecycleEvent::kNewGame);

    auto preLoad = contract.HandleEvent(LifecycleEvent::kPreLoadGame);
    CHECK(preLoad.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kLoading);

    auto failedLoad =
        contract.HandleEvent(LifecycleEvent::kPostLoadGameFailure);
    CHECK_FALSE(failedLoad.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(contract.CurrentPlayContextId());

    contract.HandleEvent(LifecycleEvent::kNewGame);
    auto revert = contract.HandleEvent(LifecycleEvent::kRevert);
    CHECK(revert.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("PlayContextLifecycle activates after a successful load",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle([] {
        return std::optional<std::string>("context-1");
    });
    IPlayContextLifecycle& contract = lifecycle;

    auto preLoad = contract.HandleEvent(LifecycleEvent::kPreLoadGame);
    CHECK_FALSE(preLoad.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kLoading);

    auto success =
        contract.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);
    CHECK_FALSE(success.contextInvalidated);
    REQUIRE(success.newPlayContextId.has_value());
    CHECK(contract.CurrentState() == LifecycleState::kActive);
    CHECK(contract.CurrentPlayContextId() == success.newPlayContextId);
}

TEST_CASE("PlayContextLifecycle treats repeated and out-of-order invalidation as safe",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle([] {
        return std::optional<std::string>("context-1");
    });
    IPlayContextLifecycle& contract = lifecycle;

    auto firstRevert = contract.HandleEvent(LifecycleEvent::kRevert);
    CHECK_FALSE(firstRevert.contextInvalidated);

    auto preLoad = contract.HandleEvent(LifecycleEvent::kPreLoadGame);
    CHECK_FALSE(preLoad.contextInvalidated);
    auto repeatedPreLoad = contract.HandleEvent(LifecycleEvent::kPreLoadGame);
    CHECK_FALSE(repeatedPreLoad.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kLoading);

    auto loadingRevert = contract.HandleEvent(LifecycleEvent::kRevert);
    CHECK(loadingRevert.contextInvalidated);
    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("PlayContextLifecycle preserves consistency when context creation fails",
          "[application][play_context_lifecycle]") {
    int generationCalls = 0;
    bool failCreation = false;
    PlayContextLifecycle lifecycle(
        [&generationCalls] {
            return std::optional<std::string>(
                "context-" + std::to_string(++generationCalls));
        },
        [&failCreation](std::string id) -> std::shared_ptr<PlayContext> {
            if (failCreation) {
                throw std::runtime_error("context creation failed");
            }
            return std::make_shared<PlayContext>(std::move(id));
        });
    IPlayContextLifecycle& contract = lifecycle;
    contract.HandleEvent(LifecycleEvent::kNewGame);
    auto previousId = contract.CurrentPlayContextId();
    REQUIRE(previousId.has_value());
    CHECK(*previousId == "context-1");

    failCreation = true;
    CHECK_THROWS_AS(contract.HandleEvent(LifecycleEvent::kNewGame),
                    std::runtime_error);

    CHECK(contract.CurrentState() == LifecycleState::kActive);
    CHECK(contract.CurrentPlayContextId() == previousId);

    failCreation = false;
    auto recovered = contract.HandleEvent(LifecycleEvent::kNewGame);
    REQUIRE(recovered.newPlayContextId.has_value());
    CHECK(contract.CurrentPlayContextId() == recovered.newPlayContextId);
}

TEST_CASE("PlayContextLifecycle preserves an empty state when initial creation fails",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [](std::string) -> std::shared_ptr<PlayContext> {
            throw std::runtime_error("context creation failed");
        });
    IPlayContextLifecycle& contract = lifecycle;

    CHECK_THROWS_AS(contract.HandleEvent(LifecycleEvent::kNewGame),
                    std::runtime_error);
    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(contract.CurrentPlayContextId());
}

TEST_CASE("PlayContextLifecycle preserves loading state when load creation fails",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [](std::string) -> std::shared_ptr<PlayContext> { return nullptr; });
    IPlayContextLifecycle& contract = lifecycle;
    contract.HandleEvent(LifecycleEvent::kPreLoadGame);

    CHECK_THROWS_AS(
        contract.HandleEvent(LifecycleEvent::kPostLoadGameSuccess),
        std::runtime_error);
    CHECK(contract.CurrentState() == LifecycleState::kLoading);
    CHECK_FALSE(contract.CurrentPlayContextId());
}

TEST_CASE("PlayContextLifecycle handles absent and throwing ID generators",
          "[application][play_context_lifecycle]") {
    PlayContextLifecycle absentGenerator(PlayContextLifecycleIdGenerator{});
    IPlayContextLifecycle& absentContract = absentGenerator;
    auto absent = absentContract.HandleEvent(LifecycleEvent::kNewGame);
    CHECK_FALSE(absent.newPlayContextId);
    CHECK(absentContract.CurrentState() == LifecycleState::kNoContext);

    PlayContextLifecycle throwingGenerator([]() -> std::optional<std::string> {
        throw std::runtime_error("ID generation failed");
    });
    IPlayContextLifecycle& throwingContract = throwingGenerator;
    CHECK_THROWS_AS(
        throwingContract.HandleEvent(LifecycleEvent::kNewGame),
        std::runtime_error);
    CHECK(throwingContract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(throwingContract.CurrentPlayContextId());

    PlayContextLifecycle unavailableFactory(
        [] { return std::optional<std::string>("context-1"); },
        PlayContextFactory{});
    IPlayContextLifecycle& unavailableContract = unavailableFactory;
    CHECK_THROWS_AS(
        unavailableContract.HandleEvent(LifecycleEvent::kNewGame),
        std::runtime_error);
    CHECK(unavailableContract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(unavailableContract.CurrentPlayContextId());
}

TEST_CASE("PlayContextLifecycle clears state when ID generation fails",
          "[application][play_context_lifecycle]") {
    int calls = 0;
    PlayContextLifecycle lifecycle([&calls] {
        if (++calls == 1) {
            return std::optional<std::string>("context-1");
        }
        return std::optional<std::string>{};
    });
    IPlayContextLifecycle& contract = lifecycle;
    contract.HandleEvent(LifecycleEvent::kNewGame);

    auto transition = contract.HandleEvent(LifecycleEvent::kNewGame);
    CHECK(transition.contextInvalidated);
    CHECK_FALSE(transition.newPlayContextId);
    CHECK(contract.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(contract.CurrentPlayContextId());
}

TEST_CASE("PlayContextLifecycle captures levels only in the active context",
          "[application][play_context_lifecycle]") {
    auto context = std::make_shared<PlayContext>("context-1");
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [context](std::string) { return context; });
    IPlayContextLifecycle& contract = lifecycle;

    contract.CaptureLevel(12);
    contract.HandleEvent(LifecycleEvent::kNewGame);
    contract.CaptureLevel(13);

    CHECK(context->characterState.CurrentCharacterSnapshot().level == 13);

    contract.HandleEvent(LifecycleEvent::kPreLoadGame);
    contract.CaptureLevel(14);
    CHECK(context->characterState.CurrentCharacterSnapshot().level == 13);

    contract.HandleEvent(LifecycleEvent::kPostLoadGameFailure);
    contract.CaptureLevel(15);
    CHECK(context->characterState.CurrentCharacterSnapshot().level == 13);

    contract.HandleEvent(LifecycleEvent::kRevert);
    contract.CaptureLevel(14);
}

TEST_CASE("PlayContextLifecycle forwards unavailable levels to active state",
          "[application][play_context_lifecycle]") {
    auto context = std::make_shared<PlayContext>("context-1");
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [context](std::string) { return context; });
    IPlayContextLifecycle& contract = lifecycle;
    contract.HandleEvent(LifecycleEvent::kNewGame);
    contract.CaptureLevel(12);
    contract.CaptureLevel(std::nullopt);

    CHECK_FALSE(context->characterState.CurrentCharacterSnapshot().level);
}

TEST_CASE("PlayContextLifecycle serializes concurrent lifecycle events",
          "[application][play_context_lifecycle]") {
    std::atomic<int> nextId{1};
    std::atomic<int> activeFactories{0};
    std::atomic<int> maximumFactories{0};
    PlayContextLifecycle lifecycle(
        [&nextId] {
            return std::optional<std::string>(
                "context-" + std::to_string(nextId.fetch_add(1)));
        },
        [&activeFactories, &maximumFactories](std::string id) {
            const int active = activeFactories.fetch_add(1) + 1;
            int maximum = maximumFactories.load();
            while (maximum < active &&
                   !maximumFactories.compare_exchange_weak(maximum, active)) {
            }
            std::this_thread::yield();
            auto context = std::make_shared<PlayContext>(std::move(id));
            activeFactories.fetch_sub(1);
            return context;
        });

    std::vector<std::thread> threads;
    for (int i = 0; i < 16; ++i) {
        threads.emplace_back([&lifecycle] {
            lifecycle.HandleEvent(LifecycleEvent::kNewGame);
        });
    }
    for (auto& thread : threads) {
        thread.join();
    }

    CHECK(maximumFactories == 1);
    REQUIRE(lifecycle.CurrentPlayContextId().has_value());
}
