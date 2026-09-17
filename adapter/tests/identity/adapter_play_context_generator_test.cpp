#include "identity/adapter_play_context_generator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <mutex>
#include <thread>
#include <vector>

using dovahlink::adapter::identity::AdapterPlayContextGenerator;

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
