#include "identity/adapter_instance_id_generator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <mutex>
#include <thread>
#include <vector>

using dovahlink::adapter::identity::AdapterInstanceId;
using dovahlink::adapter::identity::AdapterInstanceIdGenerator;

TEST_CASE("AdapterInstanceIdGenerator produces distinct ids across "
          "successive calls on one instance",
          "[identity][adapter_instance_id_generator]") {
  AdapterInstanceIdGenerator generator;

  AdapterInstanceId first = generator.Generate();
  AdapterInstanceId second = generator.Generate();

  CHECK_FALSE(first == second);
}

TEST_CASE("AdapterInstanceIdGenerator produces distinct ids across separate "
          "instances",
          "[identity][adapter_instance_id_generator]") {
  AdapterInstanceIdGenerator firstGenerator;
  AdapterInstanceIdGenerator secondGenerator;

  AdapterInstanceId first = firstGenerator.Generate();
  AdapterInstanceId second = secondGenerator.Generate();

  CHECK_FALSE(first == second);
}

TEST_CASE("AdapterInstanceIdGenerator produces distinct ids when called "
          "concurrently from multiple threads",
          "[identity][adapter_instance_id_generator]") {
  //  The production engine is thread_local specifically so concurrent
  //  callers on different threads never share mutable RNG state; this test
  //  exercises that concurrency directly rather than only sequential calls.
  constexpr int kThreadCount = 4;
  constexpr int kIdsPerThread = 25;

  std::mutex generatedMutex;
  std::vector<AdapterInstanceId> generated;
  std::vector<std::thread> threads;

  for (int threadIndex = 0; threadIndex < kThreadCount; ++threadIndex) {
    threads.emplace_back([&] {
      AdapterInstanceIdGenerator generator;
      std::vector<AdapterInstanceId> local;
      local.reserve(kIdsPerThread);
      for (int index = 0; index < kIdsPerThread; ++index) {
        local.push_back(generator.Generate());
      }

      std::lock_guard<std::mutex> lock(generatedMutex);
      generated.insert(generated.end(), local.begin(), local.end());
    });
  }

  for (std::thread &thread : threads) {
    thread.join();
  }

  REQUIRE(generated.size() == kThreadCount * kIdsPerThread);
  for (std::size_t i = 0; i < generated.size(); ++i) {
    for (std::size_t j = i + 1; j < generated.size(); ++j) {
      CHECK_FALSE(generated[i] == generated[j]);
    }
  }
}
