#include "identity/adapter_play_context_generator.hpp"
#include "identity/adapter_play_context_state.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <mutex>
#include <optional>
#include <thread>
#include <vector>

using dovahlink::adapter::identity::AdapterPlayContextGenerator;
using dovahlink::adapter::identity::AdapterPlayContextState;

TEST_CASE("AdapterPlayContextGenerator produces distinct ids across "
          "successive calls on one instance",
          "[identity][adapter_play_context_generator]") {
    AdapterPlayContextGenerator generator;

    std::array<std::byte, 16> first = generator.Generate();
    std::array<std::byte, 16> second = generator.Generate();

    CHECK_FALSE(first == second);
}

TEST_CASE("AdapterPlayContextGenerator produces distinct ids across separate "
          "instances",
          "[identity][adapter_play_context_generator]") {
    AdapterPlayContextGenerator firstGenerator;
    AdapterPlayContextGenerator secondGenerator;

    std::array<std::byte, 16> first = firstGenerator.Generate();
    std::array<std::byte, 16> second = secondGenerator.Generate();

    CHECK_FALSE(first == second);
}

TEST_CASE("AdapterPlayContextGenerator never produces an all-zero id",
          "[identity][adapter_play_context_generator]") {
    //  All-zero is reserved elsewhere (see IpcFrameCodec's rejection of an
    //  empty PlayContextChanged id) as a value no real generated id may ever
    //  collide with. A large sample is a probabilistic sanity check on that
    //  property, the same rigor this file's own distinctness tests already
    //  rely on -- it cannot deterministically exercise Generate()'s internal
    //  re-roll branch, since this generator has no injectable RNG seam (by
    //  design, matching AdapterInstanceIdGenerator's identical engine).
    AdapterPlayContextGenerator generator;
    constexpr int kDrawCount = 100'000;

    for (int i = 0; i < kDrawCount; ++i) {
        CHECK_FALSE(generator.Generate() == std::array<std::byte, 16>{});
    }
}

TEST_CASE("AdapterPlayContextGenerator produces distinct ids when called "
          "concurrently from multiple threads",
          "[identity][adapter_play_context_generator]") {
    //  The production engine is thread_local specifically so concurrent
    //  callers on different threads never share mutable RNG state; this test
    //  exercises that concurrency directly rather than only sequential calls.
    constexpr int kThreadCount = 4;
    constexpr int kIdsPerThread = 25;

    std::mutex generatedMutex;
    std::vector<std::array<std::byte, 16>> generated;
    std::vector<std::thread> threads;

    for (int threadIndex = 0; threadIndex < kThreadCount; ++threadIndex) {
        threads.emplace_back([&] {
            AdapterPlayContextGenerator generator;
            std::vector<std::array<std::byte, 16>> local;
            local.reserve(kIdsPerThread);
            for (int index = 0; index < kIdsPerThread; ++index) {
                local.push_back(generator.Generate());
            }

            std::lock_guard<std::mutex> lock(generatedMutex);
            generated.insert(generated.end(), local.begin(), local.end());
        });
    }

    for (std::thread& thread : threads) {
        thread.join();
    }

    REQUIRE(generated.size() == kThreadCount * kIdsPerThread);
    for (std::size_t i = 0; i < generated.size(); ++i) {
        for (std::size_t j = i + 1; j < generated.size(); ++j) {
            CHECK_FALSE(generated[i] == generated[j]);
        }
    }
}

//  TODO(stage4-file-extraction): Move these AdapterPlayContextState tests
//  back to their own tests/identity/adapter_play_context_state_test.cpp in
//  the post-Stage-4 structural cleanup PR. Temporarily colocated here to
//  hold this PR's changed-file count down; extraction only, no behavior
//  change.
TEST_CASE("AdapterPlayContextState reports nullopt before any value is set",
          "[identity][adapter_play_context_state]") {
    AdapterPlayContextState state;

    CHECK(state.CurrentPlayContext() == std::nullopt);
}

TEST_CASE("AdapterPlayContextState reports the most recently set value",
          "[identity][adapter_play_context_state]") {
    AdapterPlayContextState state;
    std::array<std::byte, 16> first{};
    first[0] = std::byte{1};
    std::array<std::byte, 16> second{};
    second[0] = std::byte{2};

    state.SetCurrentPlayContext(first);
    CHECK(state.CurrentPlayContext() == first);

    state.SetCurrentPlayContext(second);
    CHECK(state.CurrentPlayContext() == second);
}

TEST_CASE("AdapterPlayContextState reports nullopt again after being cleared",
          "[identity][adapter_play_context_state]") {
    AdapterPlayContextState state;
    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{1};
    state.SetCurrentPlayContext(playContextId);
    REQUIRE(state.CurrentPlayContext() == playContextId);

    state.ClearCurrentPlayContext();

    CHECK(state.CurrentPlayContext() == std::nullopt);
}

TEST_CASE("AdapterPlayContextState clearing an already-clear state stays "
          "nullopt",
          "[identity][adapter_play_context_state]") {
    AdapterPlayContextState state;

    state.ClearCurrentPlayContext();

    CHECK(state.CurrentPlayContext() == std::nullopt);
}

TEST_CASE("AdapterPlayContextState tolerates concurrent readers and a "
          "concurrent writer without tearing a value",
          "[identity][adapter_play_context_state]") {
    //  Every byte of a set value is the same repeated byte, so a reader that
    //  observes a torn write (some bytes from one set, some from another)
    //  would see a non-uniform array -- exactly what this test would catch
    //  and a plain, unguarded field would risk.
    AdapterPlayContextState state;
    constexpr int kWriteCount = 200;
    std::vector<std::thread> threads;

    threads.emplace_back([&] {
        for (int value = 1; value <= kWriteCount; ++value) {
            std::array<std::byte, 16> playContextId{};
            playContextId.fill(static_cast<std::byte>(value));
            state.SetCurrentPlayContext(playContextId);
        }
    });
    for (int reader = 0; reader < 4; ++reader) {
        threads.emplace_back([&] {
            for (int iteration = 0; iteration < kWriteCount; ++iteration) {
                //  nullopt (before the writer's first set) falls back to
                //  all-zero, which is just as uniform as any real set value.
                std::array<std::byte, 16> observed =
                    state.CurrentPlayContext().value_or(
                        std::array<std::byte, 16>{});
                std::byte first = observed[0];
                for (std::byte value : observed) {
                    CHECK(value == first);
                }
            }
        });
    }

    for (std::thread& thread : threads) {
        thread.join();
    }
}
