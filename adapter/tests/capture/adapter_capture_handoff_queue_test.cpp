#include "capture/adapter_capture_handoff_queue.hpp"

#include "capture/adapter_capture_constants.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <future>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>

using dovahlink::adapter::capture::AdapterCaptureHandoffQueue;
using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::kMaxAdapterCaptureQueueItems;

namespace {

///  A test-only gate that blocks one thread until released by another,
///  without relying on a timing sleep to prove ordering.
class BlockingGate {
public:
  ///  Blocks the calling thread until `Release` is called.
  void WaitUntilReleased() {
    std::unique_lock<std::mutex> lock(mutex_);
    condition_.wait(lock, [this] { return released_; });
  }

  ///  Releases every thread currently blocked in `WaitUntilReleased`.
  void Release() {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      released_ = true;
    }
    condition_.notify_all();
  }

private:
  ///  Guards `released_`.
  std::mutex mutex_;
  ///  Signaled when the gate is released.
  std::condition_variable condition_;
  ///  Whether `Release` has been called.
  bool released_ = false;
};

///  Builds a representative work item for a given intent key.
AdapterCaptureWorkItem BuildWorkItem(std::uint32_t intentKey) {
  return AdapterCaptureWorkItem{.intentKey = intentKey,
                                .capturedValue = {std::byte{0x01}}};
}

} //  namespace

TEST_CASE("AdapterCaptureHandoffQueue drains an accepted item on the worker "
          "thread") {
  std::promise<AdapterCaptureWorkItem> drainedPromise;
  std::future<AdapterCaptureWorkItem> drainedFuture =
      drainedPromise.get_future();
  std::thread::id drainedThreadId{};

  AdapterCaptureHandoffQueue queue(
      [&](const AdapterCaptureWorkItem &item) {
        drainedThreadId = std::this_thread::get_id();
        drainedPromise.set_value(item);
      },
      [](const AdapterCaptureWorkItem &) {});

  REQUIRE(queue.TryEnqueue(BuildWorkItem(7)));

  AdapterCaptureWorkItem drained =
      drainedFuture.wait_for(std::chrono::seconds(5)) ==
              std::future_status::ready
          ? drainedFuture.get()
          : AdapterCaptureWorkItem{};

  REQUIRE(drained == BuildWorkItem(7));
  REQUIRE(drainedThreadId != std::this_thread::get_id());
}

TEST_CASE("AdapterCaptureHandoffQueue rejects without blocking once full, "
          "and never drains the rejected item") {
  BlockingGate gate;
  std::promise<void> workerBlockedPromise;
  std::future<void> workerBlockedFuture = workerBlockedPromise.get_future();
  std::mutex drainedMutex;
  std::vector<AdapterCaptureWorkItem> drainedItems;
  std::vector<AdapterCaptureWorkItem> rejectedItems;

  AdapterCaptureHandoffQueue queue(
      [&](const AdapterCaptureWorkItem &item) {
        {
          std::lock_guard<std::mutex> lock(drainedMutex);
          drainedItems.push_back(item);
        }
        workerBlockedPromise.set_value();
        gate.WaitUntilReleased();
      },
      [&](const AdapterCaptureWorkItem &item) {
        rejectedItems.push_back(item);
      });

  //  The first item is picked up immediately and blocks the worker inside
  //  onDrained, so it never counts against the queue's own capacity.
  REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
  REQUIRE(workerBlockedFuture.wait_for(std::chrono::seconds(5)) ==
          std::future_status::ready);

  std::vector<AdapterCaptureWorkItem> fillItems;
  for (std::uint32_t index = 0; index < kMaxAdapterCaptureQueueItems; ++index) {
    fillItems.push_back(BuildWorkItem(100 + index));
    REQUIRE(queue.TryEnqueue(fillItems.back()));
  }

  REQUIRE_FALSE(queue.TryEnqueue(BuildWorkItem(999)));
  REQUIRE(rejectedItems.size() == 1);
  REQUIRE(rejectedItems.front() == BuildWorkItem(999));

  gate.Release();
  queue.Stop();

  std::lock_guard<std::mutex> lock(drainedMutex);
  std::vector<AdapterCaptureWorkItem> expectedDrained{BuildWorkItem(1)};
  expectedDrained.insert(expectedDrained.end(), fillItems.begin(),
                         fillItems.end());
  REQUIRE(drainedItems == expectedDrained);
}

TEST_CASE("AdapterCaptureHandoffQueue::Stop drains pending items, in FIFO "
          "order, before returning") {
  std::mutex drainedMutex;
  std::vector<AdapterCaptureWorkItem> drainedItems;

  AdapterCaptureHandoffQueue queue(
      [&](const AdapterCaptureWorkItem &item) {
        std::lock_guard<std::mutex> lock(drainedMutex);
        drainedItems.push_back(item);
      },
      [](const AdapterCaptureWorkItem &) {});

  std::vector<AdapterCaptureWorkItem> enqueuedItems;
  for (std::uint32_t index = 0; index < 5; ++index) {
    enqueuedItems.push_back(BuildWorkItem(index));
    REQUIRE(queue.TryEnqueue(enqueuedItems.back()));
  }

  queue.Stop();

  std::lock_guard<std::mutex> lock(drainedMutex);
  REQUIRE(drainedItems == enqueuedItems);
}

TEST_CASE("AdapterCaptureHandoffQueue keeps draining after onDrained throws "
          "for an earlier item") {
  std::mutex drainedMutex;
  std::vector<AdapterCaptureWorkItem> drainedItems;

  AdapterCaptureHandoffQueue queue(
      [&](const AdapterCaptureWorkItem &item) {
        {
          std::lock_guard<std::mutex> lock(drainedMutex);
          drainedItems.push_back(item);
        }
        if (item.intentKey == 1) {
          throw std::runtime_error("onDrained failed for item 1");
        }
      },
      [](const AdapterCaptureWorkItem &) {});

  REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
  REQUIRE(queue.TryEnqueue(BuildWorkItem(2)));

  queue.Stop();

  std::lock_guard<std::mutex> lock(drainedMutex);
  REQUIRE(drainedItems == std::vector<AdapterCaptureWorkItem>{
                              BuildWorkItem(1), BuildWorkItem(2)});
}

TEST_CASE("AdapterCaptureHandoffQueue::TryEnqueue does not propagate an "
          "exception thrown by onRejected") {
  AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem &) {},
                                   [](const AdapterCaptureWorkItem &) {
                                     throw std::runtime_error(
                                         "onRejected failed");
                                   });

  queue.Stop();

  bool accepted = true;
  REQUIRE_NOTHROW(accepted = queue.TryEnqueue(BuildWorkItem(1)));
  REQUIRE_FALSE(accepted);
}

TEST_CASE("AdapterCaptureHandoffQueue::Stop is idempotent") {
  AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem &) {},
                                   [](const AdapterCaptureWorkItem &) {});

  queue.Stop();
  queue.Stop();
}

TEST_CASE("AdapterCaptureHandoffQueue rejects new items after Stop") {
  AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem &) {},
                                   [](const AdapterCaptureWorkItem &) {});

  queue.Stop();

  REQUIRE_FALSE(queue.TryEnqueue(BuildWorkItem(1)));
}
