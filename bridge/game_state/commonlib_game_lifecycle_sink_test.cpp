#include "game_state/commonlib_game_lifecycle_sink.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {

using dovahlink::application::IActivePlayContext;
using dovahlink::application::PlayContext;

///  Thread-safe active-context double that exposes overlapping mutations.
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
        for (int yield = 0; yield < 64; ++yield) {
            std::this_thread::yield();
        }
        std::lock_guard<std::mutex> lock(mutex_);
        current_.reset();
        LeaveMutation();
    }

    ///  Replaces the currently published context.
    std::shared_ptr<PlayContext> Begin(std::string id) override {
        return Replace(std::move(id));
    }

    ///  Records concurrent mutation attempts while publishing the context.
    std::shared_ptr<PlayContext> Replace(std::string id) override {
        EnterMutation();

        for (int yield = 0; yield < 64; ++yield) {
            std::this_thread::yield();
        }

        if (throwNextReplacement_.exchange(false)) {
            LeaveMutation();
            throw std::runtime_error("replacement failed");
        }

        auto context = std::make_shared<PlayContext>(std::move(id));
        {
            std::lock_guard<std::mutex> lock(mutex_);
            current_ = context;
            replacedIds_.push_back(context->id);
        }
        LeaveMutation();
        return context;
    }

    ///  Makes the next replacement fail after entering the mutation window.
    void ThrowNextReplacement() {
        throwNextReplacement_.store(true);
    }

    ///  Returns the greatest number of concurrent mutations observed.
    [[nodiscard]] int MaximumConcurrentMutations() const {
        return maximumConcurrentMutations_.load();
    }

    ///  Returns the number of replacement calls observed.
    [[nodiscard]] std::size_t ReplacementCount() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return replacedIds_.size();
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

    ///  Records exit from the mutation window.
    void LeaveMutation() { activeMutations_.fetch_sub(1); }

    ///  Synchronizes published context and diagnostic replacement IDs.
    mutable std::mutex mutex_;

    ///  Currently published context.
    std::shared_ptr<PlayContext> current_;

    ///  IDs published by replacement calls, in completion order.
    std::vector<std::string> replacedIds_;

    ///  Number of replacement calls currently in progress.
    std::atomic<int> activeMutations_{0};

    ///  Greatest number of replacement calls observed at once.
    std::atomic<int> maximumConcurrentMutations_{0};

    ///  Whether the next replacement should report a controlled failure.
    std::atomic<bool> throwNextReplacement_{false};
};

} //  namespace

TEST_CASE("CommonLibGameLifecycleSink serializes tracker and context updates",
          "[game_state][lifecycle]") {
    using dovahlink::application::GameLifecycleTracker;

    std::atomic<int> nextId{1};
    GameLifecycleTracker tracker([&nextId] {
        return std::optional<std::string>(
            "context-" + std::to_string(nextId.fetch_add(1)));
    });
    ThreadSafeActivePlayContextFake activePlayContext;
    dovahlink::game_state::CommonLibGameLifecycleSink sink(tracker,
                                                           activePlayContext);
    sink.Register([](dovahlink::application::ContainedWork work) {
        work();
        return true;
    });

    constexpr int kConcurrentEvents = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::vector<std::thread> threads;
    threads.reserve(kConcurrentEvents);

    for (int i = 0; i < kConcurrentEvents; ++i) {
        threads.emplace_back([&] {
            readyCount.fetch_add(1, std::memory_order_release);
            while (!go.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            SKSE::MessagingInterface::Message message{
                .sender = nullptr,
                .type = SKSE::MessagingInterface::kNewGame,
                .dataLen = 0,
                .data = nullptr,
            };
            sink.OnMessage(message);
        });
    }

    while (readyCount.load(std::memory_order_acquire) < kConcurrentEvents) {
        std::this_thread::yield();
    }
    go.store(true, std::memory_order_release);

    for (std::thread& thread : threads) {
        thread.join();
    }

    CHECK(activePlayContext.ReplacementCount() == kConcurrentEvents);
    CHECK(activePlayContext.MaximumConcurrentMutations() == 1);
    CHECK(tracker.CurrentState() ==
          dovahlink::application::LifecycleState::kActive);
    REQUIRE(tracker.CurrentPlayContextId().has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
}

TEST_CASE("CommonLibGameLifecycleSink serializes reset and replacement sources",
          "[game_state][lifecycle]") {
    using dovahlink::application::GameLifecycleTracker;
    using dovahlink::application::LifecycleEvent;

    std::atomic<int> nextId{1};
    GameLifecycleTracker tracker([&nextId] {
        return std::optional<std::string>(
            "context-" + std::to_string(nextId.fetch_add(1)));
    });
    ThreadSafeActivePlayContextFake activePlayContext;
    dovahlink::game_state::CommonLibGameLifecycleSink sink(tracker,
                                                           activePlayContext);
    sink.Register([](dovahlink::application::ContainedWork work) {
        work();
        return true;
    });

    SKSE::MessagingInterface::Message newGameMessage{
        .sender = nullptr,
        .type = SKSE::MessagingInterface::kNewGame,
        .dataLen = 0,
        .data = nullptr,
    };
    SKSE::MessagingInterface::Message revertMessage{
        .sender = nullptr,
        .type = SKSE::MessagingInterface::kPreLoadGame,
        .dataLen = 0,
        .data = nullptr,
    };

    sink.OnMessage(newGameMessage);

    constexpr int kConcurrentEvents = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::vector<std::thread> threads;
    threads.reserve(kConcurrentEvents);
    for (int i = 0; i < kConcurrentEvents; ++i) {
        threads.emplace_back([&, i] {
            readyCount.fetch_add(1, std::memory_order_release);
            while (!go.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            sink.OnMessage(i % 2 == 0 ? newGameMessage : revertMessage);
        });
    }

    while (readyCount.load(std::memory_order_acquire) < kConcurrentEvents) {
        std::this_thread::yield();
    }
    go.store(true, std::memory_order_release);
    for (std::thread& thread : threads) {
        thread.join();
    }

    CHECK(activePlayContext.MaximumConcurrentMutations() == 1);
    auto trackedId = tracker.CurrentPlayContextId();
    auto activeId = activePlayContext.CurrentContextId();
    CHECK(activeId == trackedId);
}

TEST_CASE("CommonLibGameLifecycleSink releases serialization after a contained "
          "transition failure",
          "[game_state][lifecycle]") {
    using dovahlink::application::GameLifecycleTracker;

    std::atomic<int> nextId{1};
    GameLifecycleTracker tracker([&nextId] {
        return std::optional<std::string>(
            "context-" + std::to_string(nextId.fetch_add(1)));
    });
    ThreadSafeActivePlayContextFake activePlayContext;
    activePlayContext.ThrowNextReplacement();
    dovahlink::game_state::CommonLibGameLifecycleSink sink(tracker,
                                                           activePlayContext);
    sink.Register([](dovahlink::application::ContainedWork work) {
        try {
            work();
            return true;
        } catch (...) {
            return false;
        }
    });

    SKSE::MessagingInterface::Message message{
        .sender = nullptr,
        .type = SKSE::MessagingInterface::kNewGame,
        .dataLen = 0,
        .data = nullptr,
    };
    sink.OnMessage(message);
    sink.OnMessage(message);

    REQUIRE(tracker.CurrentPlayContextId().has_value());
    CHECK(activePlayContext.CurrentContextId() ==
          tracker.CurrentPlayContextId());
    CHECK(activePlayContext.ReplacementCount() == 1);
}
