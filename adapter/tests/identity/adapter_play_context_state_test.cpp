#include "identity/adapter_play_context_state.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <optional>
#include <thread>
#include <vector>

using dovahlink::adapter::identity::AdapterPlayContextState;

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
