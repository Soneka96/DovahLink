#include "application/lifecycle_transition_coordinator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <barrier>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::GameLifecycleTracker;
using dovahlink::application::IActivePlayContext;
using dovahlink::application::LifecycleEvent;
using dovahlink::application::LifecycleState;
using dovahlink::application::LifecycleTransitionCoordinator;
using dovahlink::application::PlayContext;

namespace {

///  Thread-safe active-context double that exposes mutation overlap.
class ThreadSafeActivePlayContextFake final : public IActivePlayContext {
  public:
    ///  Returns the currently published context.
    [[nodiscard]] std::shared_ptr<PlayContext>
    AcquireCurrent() const override {
        std::lock_guard<std::mutex> lock(mutex_);
        return current_;
    }

    ///  Clears the currently published context.
    void Reset() override {
        EnterMutation();
        YieldDuringMutation();
        if (throwNextReset_.exchange(false)) {
            LeaveMutation();
            throw std::runtime_error("reset failed");
        }
        {
            std::lock_guard<std::mutex> lock(mutex_);
            current_.reset();
        }
        LeaveMutation();
    }

    ///  Replaces the currently published context.
    std::shared_ptr<PlayContext> Begin(std::string id) override {
        return Replace(std::move(id));
    }

    ///  Records and publishes one replacement.
    std::shared_ptr<PlayContext> Replace(std::string id) override {
        EnterMutation();
        YieldDuringMutation();
        if (throwNextReplacement_.exchange(false)) {
            LeaveMutation();
            throw std::runtime_error("replacement failed");
        }

        auto context = std::make_shared<PlayContext>(std::move(id));
        {
            std::lock_guard<std::mutex> lock(mutex_);
            current_ = context;
            ++replacementCount_;
        }
        LeaveMutation();
        return context;
    }

    ///  Makes the next replacement fail after entering the mutation window.
    void ThrowNextReplacement() {
        throwNextReplacement_.store(true);
    }

    ///  Makes the next reset fail after entering the mutation window.
    void ThrowNextReset() { throwNextReset_.store(true); }

    ///  Returns the greatest number of concurrent mutations observed.
    [[nodiscard]] int MaximumConcurrentMutations() const {
        return maximumConcurrentMutations_.load();
    }

    ///  Returns the number of successful replacements.
    [[nodiscard]] int ReplacementCount() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return replacementCount_;
    }

    ///  Returns the currently published context ID, if any.
    [[nodiscard]] std::optional<std::string> CurrentContextId() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return current_ ? std::optional<std::string>(current_->id)
                        : std::nullopt;
    }

  private:
    ///  Records entry into the mutation window and updates its maximum overlap.
    void EnterMutation() {
        const int active = activeMutations_.fetch_add(1) + 1;
        int observedMaximum = maximumConcurrentMutations_.load();
        while (observedMaximum < active &&
               !maximumConcurrentMutations_.compare_exchange_weak(
                   observedMaximum, active)) {
        }
    }

    ///  Widens the mutation window without using a timing sleep.
    static void YieldDuringMutation() {
        for (int yield = 0; yield < 64; ++yield) {
            std::this_thread::yield();
        }
    }

    ///  Records exit from the mutation window.
    void LeaveMutation() { activeMutations_.fetch_sub(1); }

    ///  Synchronizes published context and replacement count.
    mutable std::mutex mutex_;

    ///  Currently published context.
    std::shared_ptr<PlayContext> current_;

    ///  Number of successful replacements.
    int replacementCount_ = 0;

    ///  Number of replacement/reset calls currently in progress.
    std::atomic<int> activeMutations_{0};

    ///  Greatest number of concurrent mutations observed.
    std::atomic<int> maximumConcurrentMutations_{0};

    ///  Whether the next replacement should report a controlled failure.
    std::atomic<bool> throwNextReplacement_{false};

    ///  Whether the next reset should report a controlled failure.
    std::atomic<bool> throwNextReset_{false};
};

///  Builds a tracker with deterministic unique context IDs.
GameLifecycleTracker BuildTracker(std::atomic<int>& nextId) {
    return GameLifecycleTracker([&nextId] {
        return std::optional<std::string>(
            "context-" + std::to_string(nextId.fetch_add(1)));
    });
}

///  Runs concurrent lifecycle events after all worker threads reach a barrier.
template <typename EventSelector>
void RunConcurrentEvents(LifecycleTransitionCoordinator& coordinator,
                         int eventCount, EventSelector selectEvent) {
    std::barrier startBarrier(eventCount + 1);
    std::vector<std::thread> threads;
    threads.reserve(eventCount);
    for (int i = 0; i < eventCount; ++i) {
        threads.emplace_back([&, i] {
            startBarrier.arrive_and_wait();
            coordinator.HandleEvent(selectEvent(i));
        });
    }

    startBarrier.arrive_and_wait();
    for (std::thread& thread : threads) {
        thread.join();
    }
}

} //  namespace

TEST_CASE("LifecycleTransitionCoordinator serializes concurrent replacements",
          "[application][lifecycle_transition_coordinator]") {
    std::atomic<int> nextId{1};
    auto tracker = BuildTracker(nextId);
    ThreadSafeActivePlayContextFake activePlayContext;
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);

    constexpr int kConcurrentEvents = 16;
    RunConcurrentEvents(coordinator, kConcurrentEvents,
                        [](int) { return LifecycleEvent::kNewGame; });

    CHECK(activePlayContext.ReplacementCount() == kConcurrentEvents);
    CHECK(activePlayContext.MaximumConcurrentMutations() == 1);
    REQUIRE(tracker.CurrentPlayContextId().has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
}

TEST_CASE("LifecycleTransitionCoordinator serializes reset and replacement events",
          "[application][lifecycle_transition_coordinator]") {
    std::atomic<int> nextId{1};
    auto tracker = BuildTracker(nextId);
    ThreadSafeActivePlayContextFake activePlayContext;
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);
    coordinator.HandleEvent(LifecycleEvent::kNewGame);

    constexpr int kConcurrentEvents = 16;
    RunConcurrentEvents(coordinator, kConcurrentEvents, [](int index) {
        return index % 2 == 0 ? LifecycleEvent::kNewGame
                              : LifecycleEvent::kRevert;
    });

    CHECK(activePlayContext.MaximumConcurrentMutations() == 1);
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
}

TEST_CASE("LifecycleTransitionCoordinator applies every lifecycle outcome",
          "[application][lifecycle_transition_coordinator]") {
    std::atomic<int> nextId{1};
    auto tracker = BuildTracker(nextId);
    ThreadSafeActivePlayContextFake activePlayContext;
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);

    auto newGame = coordinator.HandleEvent(LifecycleEvent::kNewGame);
    CHECK_FALSE(newGame.contextInvalidated);
    REQUIRE(newGame.newPlayContextId.has_value());
    CHECK(activePlayContext.CurrentContextId() == newGame.newPlayContextId);

    auto newGameReplacement = coordinator.HandleEvent(LifecycleEvent::kNewGame);
    CHECK(newGameReplacement.contextInvalidated);
    REQUIRE(newGameReplacement.newPlayContextId.has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          newGameReplacement.newPlayContextId);

    auto preLoad = coordinator.HandleEvent(LifecycleEvent::kPreLoadGame);
    CHECK(preLoad.contextInvalidated);
    CHECK_FALSE(preLoad.newPlayContextId.has_value());
    CHECK_FALSE(activePlayContext.CurrentContextId().has_value());

    auto failedLoad =
        coordinator.HandleEvent(LifecycleEvent::kPostLoadGameFailure);
    CHECK_FALSE(failedLoad.contextInvalidated);
    CHECK_FALSE(failedLoad.newPlayContextId.has_value());
    CHECK_FALSE(activePlayContext.CurrentContextId().has_value());

    auto successfulLoad =
        coordinator.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);
    CHECK_FALSE(successfulLoad.contextInvalidated);
    REQUIRE(successfulLoad.newPlayContextId.has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          successfulLoad.newPlayContextId);

    auto successfulLoadReplacement =
        coordinator.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);
    CHECK(successfulLoadReplacement.contextInvalidated);
    REQUIRE(successfulLoadReplacement.newPlayContextId.has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          successfulLoadReplacement.newPlayContextId);

    auto revert = coordinator.HandleEvent(LifecycleEvent::kRevert);
    CHECK(revert.contextInvalidated);
    CHECK_FALSE(revert.newPlayContextId.has_value());
    CHECK_FALSE(activePlayContext.CurrentContextId().has_value());
}

TEST_CASE("LifecycleTransitionCoordinator clears the old context when a fresh "
          "ID cannot be generated",
          "[application][lifecycle_transition_coordinator]") {
    int generationCalls = 0;
    GameLifecycleTracker tracker([&generationCalls] {
        if (generationCalls++ == 0) {
            return std::optional<std::string>("context-1");
        }
        return std::optional<std::string>{};
    });
    ThreadSafeActivePlayContextFake activePlayContext;
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);

    auto initial = coordinator.HandleEvent(LifecycleEvent::kNewGame);
    REQUIRE(initial.newPlayContextId.has_value());
    REQUIRE(activePlayContext.CurrentContextId().has_value());

    auto failedGeneration = coordinator.HandleEvent(LifecycleEvent::kNewGame);
    CHECK(failedGeneration.contextInvalidated);
    CHECK_FALSE(failedGeneration.newPlayContextId.has_value());
    CHECK_FALSE(activePlayContext.CurrentContextId().has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kActive);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("LifecycleTransitionCoordinator releases serialization after failure",
          "[application][lifecycle_transition_coordinator]") {
    std::atomic<int> nextId{1};
    auto tracker = BuildTracker(nextId);
    ThreadSafeActivePlayContextFake activePlayContext;
    activePlayContext.ThrowNextReplacement();
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);

    CHECK_THROWS_AS(coordinator.HandleEvent(LifecycleEvent::kNewGame),
                    std::runtime_error);
    coordinator.HandleEvent(LifecycleEvent::kNewGame);

    REQUIRE(tracker.CurrentPlayContextId().has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
    CHECK(activePlayContext.ReplacementCount() == 1);
}

TEST_CASE("LifecycleTransitionCoordinator releases serialization after reset failure",
          "[application][lifecycle_transition_coordinator]") {
    std::atomic<int> nextId{1};
    auto tracker = BuildTracker(nextId);
    ThreadSafeActivePlayContextFake activePlayContext;
    LifecycleTransitionCoordinator coordinator(tracker, activePlayContext);
    coordinator.HandleEvent(LifecycleEvent::kNewGame);
    activePlayContext.ThrowNextReset();

    CHECK_THROWS_AS(coordinator.HandleEvent(LifecycleEvent::kPreLoadGame),
                    std::runtime_error);
    coordinator.HandleEvent(LifecycleEvent::kNewGame);

    REQUIRE(tracker.CurrentPlayContextId().has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
}
