#include "capture/adapter_capture_handoff_queue.hpp"
#include "capture/live_state_sample_codec.hpp"
#include "enums.hpp"

#include "constants.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <charconv>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <future>
#include <limits>
#include <mutex>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

using dovahlink::adapter::capture::AdapterCaptureHandoffQueue;
using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::CapturedPayload;
using dovahlink::adapter::capture::CharacterEventKey;
using dovahlink::adapter::capture::CharacterSampleToken;
using dovahlink::adapter::capture::EncodeFloatLittleEndian;
using dovahlink::adapter::capture::EncodeUInt16LittleEndian;
using dovahlink::adapter::capture::kMaxAdapterCaptureQueueItems;
using dovahlink::adapter::capture::kMaxCapturedPayloadBytes;
using dovahlink::adapter::capture::MakeCapturedPayload;
using dovahlink::adapter::capture::TryMakeCapturedPayload;
using dovahlink::adapter::test_support::ReadSource;

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
    return AdapterCaptureWorkItem{
        .intentKey = intentKey,
        .capturedValue = dovahlink::adapter::capture::MakeCapturedPayload(
            std::array{std::byte{0x01}})};
}

} //  namespace

TEST_CASE("AdapterCaptureHandoffQueue drains an accepted item on the worker "
          "thread") {
    std::promise<AdapterCaptureWorkItem> drainedPromise;
    std::future<AdapterCaptureWorkItem> drainedFuture =
        drainedPromise.get_future();
    std::thread::id drainedThreadId{};

    AdapterCaptureHandoffQueue queue(
        [&](const AdapterCaptureWorkItem& item) {
            drainedThreadId = std::this_thread::get_id();
            drainedPromise.set_value(item);
        },
        [](const AdapterCaptureWorkItem&) {});

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
    bool workerBlocked = false;

    AdapterCaptureHandoffQueue queue(
        [&](const AdapterCaptureWorkItem& item) {
            {
                std::lock_guard<std::mutex> lock(drainedMutex);
                drainedItems.push_back(item);
            }
            if (!workerBlocked) {
                workerBlocked = true;
                workerBlockedPromise.set_value();
                gate.WaitUntilReleased();
            }
        },
        [&](const AdapterCaptureWorkItem& item) {
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

TEST_CASE("AdapterCaptureHandoffQueue preserves FIFO order across a "
          "ring-buffer wraparound") {
    std::mutex drainedMutex;
    std::vector<AdapterCaptureWorkItem> drainedItems;
    std::condition_variable drainedCondition;

    AdapterCaptureHandoffQueue queue(
        [&](const AdapterCaptureWorkItem& item) {
            std::lock_guard<std::mutex> lock(drainedMutex);
            drainedItems.push_back(item);
            drainedCondition.notify_one();
        },
        [](const AdapterCaptureWorkItem&) {});

    //  Enqueuing and waiting for each item to drain before enqueuing the
    //  next advances the ring buffer's head index one slot at a time, past
    //  its own capacity, without ever needing more than one item present at
    //  once -- proving the modulo wraparound itself, not just FIFO order
    //  within one full buffer's worth of items.
    std::vector<AdapterCaptureWorkItem> enqueuedItems;
    for (std::uint32_t index = 0; index < kMaxAdapterCaptureQueueItems + 5;
         ++index) {
        enqueuedItems.push_back(BuildWorkItem(index));
        REQUIRE(queue.TryEnqueue(enqueuedItems.back()));

        std::unique_lock<std::mutex> lock(drainedMutex);
        REQUIRE(drainedCondition.wait_for(
            lock, std::chrono::seconds(5),
            [&] { return drainedItems.size() == index + 1; }));
    }

    queue.Stop();

    std::lock_guard<std::mutex> lock(drainedMutex);
    REQUIRE(drainedItems == enqueuedItems);
}

TEST_CASE("AdapterCaptureHandoffQueue::Stop drains pending items, in FIFO "
          "order, before returning") {
    std::mutex drainedMutex;
    std::vector<AdapterCaptureWorkItem> drainedItems;

    AdapterCaptureHandoffQueue queue(
        [&](const AdapterCaptureWorkItem& item) {
            std::lock_guard<std::mutex> lock(drainedMutex);
            drainedItems.push_back(item);
        },
        [](const AdapterCaptureWorkItem&) {});

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
        [&](const AdapterCaptureWorkItem& item) {
            {
                std::lock_guard<std::mutex> lock(drainedMutex);
                drainedItems.push_back(item);
            }
            if (item.intentKey == 1) {
                throw std::runtime_error("onDrained failed for item 1");
            }
        },
        [](const AdapterCaptureWorkItem&) {});

    REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
    REQUIRE(queue.TryEnqueue(BuildWorkItem(2)));

    queue.Stop();

    std::lock_guard<std::mutex> lock(drainedMutex);
    REQUIRE(drainedItems == std::vector<AdapterCaptureWorkItem>{
                                BuildWorkItem(1), BuildWorkItem(2)});
}

TEST_CASE("AdapterCaptureHandoffQueue::TryEnqueue does not propagate an "
          "exception thrown by onRejected") {
    AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem&) {},
                                     [](const AdapterCaptureWorkItem&) {
                                         throw std::runtime_error(
                                             "onRejected failed");
                                     });

    queue.Stop();

    bool accepted = true;
    REQUIRE_NOTHROW(accepted = queue.TryEnqueue(BuildWorkItem(1)));
    REQUIRE_FALSE(accepted);
}

TEST_CASE("AdapterCaptureHandoffQueue::Stop is idempotent") {
    AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem&) {},
                                     [](const AdapterCaptureWorkItem&) {});

    queue.Stop();
    queue.Stop();
}

TEST_CASE("AdapterCaptureHandoffQueue rejects new items after Stop") {
    std::thread::id callerThreadId = std::this_thread::get_id();
    std::thread::id rejectedThreadId;
    AdapterCaptureHandoffQueue queue([](const AdapterCaptureWorkItem&) {},
                                     [&](const AdapterCaptureWorkItem&) {
                                         rejectedThreadId =
                                             std::this_thread::get_id();
                                     });

    queue.Stop();

    REQUIRE_FALSE(queue.TryEnqueue(BuildWorkItem(1)));
    CHECK(rejectedThreadId == callerThreadId);
}

TEST_CASE("AdapterCaptureHandoffQueue safely destroys after callback-initiated "
          "Stop") {
    std::promise<void> callbackFinishedPromise;
    std::future<void> callbackFinishedFuture =
        callbackFinishedPromise.get_future();
    AdapterCaptureHandoffQueue* queuePointer = nullptr;
    std::atomic<bool> stopThrew{false};

    {
        AdapterCaptureHandoffQueue queue(
            [&](const AdapterCaptureWorkItem&) {
                try {
                    queuePointer->Stop();
                } catch (...) {
                    stopThrew = true;
                }
                callbackFinishedPromise.set_value();
            },
            [](const AdapterCaptureWorkItem&) {});
        queuePointer = &queue;

        REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
        REQUIRE(callbackFinishedFuture.wait_for(std::chrono::seconds(5)) ==
                std::future_status::ready);
    }

    CHECK_FALSE(stopThrew.load());
}

TEST_CASE("AdapterCaptureHandoffQueue drains pending items after a "
          "callback-initiated Stop") {
    std::mutex drainedMutex;
    std::vector<AdapterCaptureWorkItem> drainedItems;
    std::promise<void> firstEnteredPromise;
    std::future<void> firstEnteredFuture = firstEnteredPromise.get_future();
    std::promise<void> releaseFirstPromise;
    std::shared_future<void> releaseFirstFuture =
        releaseFirstPromise.get_future().share();
    AdapterCaptureHandoffQueue* queuePointer = nullptr;
    std::atomic<bool> stopThrew{false};

    {
        AdapterCaptureHandoffQueue queue(
            [&](const AdapterCaptureWorkItem& item) {
                {
                    std::lock_guard<std::mutex> lock(drainedMutex);
                    drainedItems.push_back(item);
                }
                if (item.intentKey == 1) {
                    firstEnteredPromise.set_value();
                    releaseFirstFuture.wait();
                    try {
                        queuePointer->Stop();
                    } catch (...) {
                        stopThrew = true;
                    }
                }
            },
            [](const AdapterCaptureWorkItem&) {});
        queuePointer = &queue;

        REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
        REQUIRE(firstEnteredFuture.wait_for(std::chrono::seconds(5)) ==
                std::future_status::ready);
        REQUIRE(queue.TryEnqueue(BuildWorkItem(2)));
        releaseFirstPromise.set_value();
    }

    CHECK_FALSE(stopThrew.load());
    std::lock_guard<std::mutex> lock(drainedMutex);
    CHECK(drainedItems == std::vector<AdapterCaptureWorkItem>{BuildWorkItem(1),
                                                              BuildWorkItem(2)});
}

TEST_CASE("AdapterCaptureHandoffQueue coordinates concurrent external Stop "
          "calls") {
    std::promise<void> callbackEnteredPromise;
    std::future<void> callbackEnteredFuture = callbackEnteredPromise.get_future();
    std::promise<void> releaseCallbackPromise;
    std::shared_future<void> releaseCallbackFuture =
        releaseCallbackPromise.get_future().share();
    std::promise<void> firstStopStartedPromise;
    std::future<void> firstStopStartedFuture =
        firstStopStartedPromise.get_future();
    std::promise<void> secondStopStartedPromise;
    std::future<void> secondStopStartedFuture =
        secondStopStartedPromise.get_future();
    std::atomic<bool> firstStopThrew{false};
    std::atomic<bool> secondStopThrew{false};

    AdapterCaptureHandoffQueue queue(
        [&](const AdapterCaptureWorkItem&) {
            callbackEnteredPromise.set_value();
            releaseCallbackFuture.wait();
        },
        [](const AdapterCaptureWorkItem&) {});

    REQUIRE(queue.TryEnqueue(BuildWorkItem(1)));
    REQUIRE(callbackEnteredFuture.wait_for(std::chrono::seconds(5)) ==
            std::future_status::ready);

    std::thread firstStopper([&] {
        firstStopStartedPromise.set_value();
        try {
            queue.Stop();
        } catch (...) {
            firstStopThrew = true;
        }
    });
    std::thread secondStopper([&] {
        secondStopStartedPromise.set_value();
        try {
            queue.Stop();
        } catch (...) {
            secondStopThrew = true;
        }
    });

    REQUIRE(firstStopStartedFuture.wait_for(std::chrono::seconds(5)) ==
            std::future_status::ready);
    REQUIRE(secondStopStartedFuture.wait_for(std::chrono::seconds(5)) ==
            std::future_status::ready);
    releaseCallbackPromise.set_value();

    firstStopper.join();
    secondStopper.join();
    CHECK_FALSE(firstStopThrew.load());
    CHECK_FALSE(secondStopThrew.load());
}

//  TODO(stage4-file-extraction): Move the live_state_sample_codec tests below
//  back to their own tests/capture/live_state_sample_codec_test.cpp in the
//  post-Stage-4 structural cleanup PR. Temporarily colocated with the
//  sibling capture-handoff-queue test to hold this PR's changed-file count
//  down; extraction only, no behavior change.
TEST_CASE("EncodeFloatLittleEndian matches the host's little-endian float decode",
          "[capture][live_state_sample_codec]") {
    //  1.5f's IEEE-754 bit pattern is 0x3FC00000; little-endian byte order
    //  places the least-significant byte first.
    std::array<std::byte, 4> encoded = EncodeFloatLittleEndian(1.5f);

    CHECK(encoded == std::array<std::byte, 4>{
                         std::byte{0x00}, std::byte{0x00}, std::byte{0xC0}, std::byte{0x3F}});
}

TEST_CASE("EncodeFloatLittleEndian round-trips zero, a negative value, and a large value",
          "[capture][live_state_sample_codec]") {
    CHECK(EncodeFloatLittleEndian(0.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x00}, std::byte{0x00}, std::byte{0x00}});
    //  -1.0f's bit pattern is 0xBF800000.
    CHECK(EncodeFloatLittleEndian(-1.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x00}, std::byte{0x80}, std::byte{0xBF}});
    //  100000.0f's bit pattern is 0x47C35000.
    CHECK(EncodeFloatLittleEndian(100000.0f) ==
          std::array<std::byte, 4>{std::byte{0x00}, std::byte{0x50}, std::byte{0xC3}, std::byte{0x47}});
}

TEST_CASE("EncodeUInt16LittleEndian places the least-significant byte first",
          "[capture][live_state_sample_codec]") {
    CHECK(EncodeUInt16LittleEndian(0x1234) ==
          std::array<std::byte, 2>{std::byte{0x34}, std::byte{0x12}});
    CHECK(EncodeUInt16LittleEndian(0) == std::array<std::byte, 2>{std::byte{0x00}, std::byte{0x00}});
    CHECK(EncodeUInt16LittleEndian(0xFFFF) ==
          std::array<std::byte, 2>{std::byte{0xFF}, std::byte{0xFF}});
}

///  Reassembles an `EncodeFloatLittleEndian` result back into the bit
///  pattern it encoded, for round-trip comparison. Test-only: production
///  code never needs to decode on the adapter side, since the host owns
///  decoding.
std::uint32_t DecodeBitsLittleEndian(const std::array<std::byte, 4>& bytes) {
    std::uint32_t bits = 0;
    for (int index = 3; index >= 0; --index) {
        bits = (bits << 8) | std::to_integer<std::uint32_t>(bytes[static_cast<std::size_t>(index)]);
    }
    return bits;
}

TEST_CASE("EncodeFloatLittleEndian round-trips every bit pattern exactly, "
          "including NaN and Infinity",
          "[capture][live_state_sample_codec]") {
    //  Compared as bit patterns, not float equality: NaN != NaN under IEEE-754,
    //  so a float-equality round-trip check would falsely fail for NaN even
    //  when the bytes are correct.
    for (float value : {0.0f, -1.0f, 1.5f, 100000.0f, 1e-30f,
                        std::numeric_limits<float>::infinity(),
                        -std::numeric_limits<float>::infinity(),
                        std::numeric_limits<float>::quiet_NaN()}) {
        std::uint32_t originalBits = std::bit_cast<std::uint32_t>(value);
        std::uint32_t roundTrippedBits = DecodeBitsLittleEndian(EncodeFloatLittleEndian(value));
        CHECK(roundTrippedBits == originalBits);
    }
}

TEST_CASE("MakeCapturedPayload copies the source bytes and records their "
          "count as size",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, 2> source{std::byte{0x34}, std::byte{0x12}};

    CapturedPayload payload = MakeCapturedPayload(source);

    REQUIRE(payload.size == 2);
    CHECK(payload.AsSpan().size() == 2);
    CHECK(payload.AsSpan()[0] == std::byte{0x34});
    CHECK(payload.AsSpan()[1] == std::byte{0x12});
}

TEST_CASE("MakeCapturedPayload leaves every byte beyond size zeroed",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, 1> source{std::byte{0xFF}};

    CapturedPayload payload = MakeCapturedPayload(source);

    for (std::size_t index = 1; index < payload.bytes.size(); ++index) {
        CHECK(payload.bytes[index] == std::byte{0x00});
    }
}

TEST_CASE("MakeCapturedPayload fills the full buffer at the maximum size",
          "[capture][live_state_sample_codec]") {
    std::array<std::byte, kMaxCapturedPayloadBytes> source{};
    for (std::size_t index = 0; index < source.size(); ++index) {
        source[index] = static_cast<std::byte>(index);
    }

    CapturedPayload payload = MakeCapturedPayload(source);

    REQUIRE(payload.size == kMaxCapturedPayloadBytes);
    CHECK(std::ranges::equal(payload.AsSpan(), source));
}

TEST_CASE("CapturedPayload equality compares both the buffer and size, "
          "treating differently-sized empty payloads as equal since their "
          "unused bytes are always zero",
          "[capture][live_state_sample_codec]") {
    CapturedPayload empty{};
    CapturedPayload alsoEmpty = MakeCapturedPayload(std::array<std::byte, 0>{});
    std::array<std::byte, 1> oneByte{std::byte{0x01}};

    CHECK(empty == alsoEmpty);
    CHECK_FALSE(empty == MakeCapturedPayload(oneByte));
}

TEST_CASE("TryMakeCapturedPayload accepts runtime spans at and under the "
          "maximum capacity, preserving exact bytes and size",
          "[capture][live_state_sample_codec]") {
    std::vector<std::byte> empty;
    std::vector<std::byte> twoBytes{std::byte{0x01}, std::byte{0x02}};
    std::vector<std::byte> fourBytes{
        std::byte{0x01}, std::byte{0x02}, std::byte{0x03}, std::byte{0x04}};
    std::vector<std::byte> twelveBytes(kMaxCapturedPayloadBytes);
    for (std::size_t index = 0; index < twelveBytes.size(); ++index) {
        twelveBytes[index] = static_cast<std::byte>(index);
    }

    for (const std::vector<std::byte>& source :
         {empty, twoBytes, fourBytes, twelveBytes}) {
        std::optional<CapturedPayload> payload =
            TryMakeCapturedPayload(std::span(source));

        REQUIRE(payload.has_value());
        REQUIRE(payload->size == source.size());
        CHECK(std::ranges::equal(payload->AsSpan(), source));
    }
}

TEST_CASE("TryMakeCapturedPayload fails closed for a runtime span over the "
          "maximum capacity, without truncating it",
          "[capture][live_state_sample_codec]") {
    std::vector<std::byte> oversized(kMaxCapturedPayloadBytes + 1);

    CHECK_FALSE(TryMakeCapturedPayload(std::span(oversized)).has_value());
}

//  TODO(stage4-file-extraction): Move this live-state-catalog-fixture test
//  back to its own tests/capture/live_state_catalog_fixture_test.cpp in the
//  post-Stage-4 structural cleanup PR. Temporarily colocated with the
//  sibling capture-codec test to hold this PR's changed-file count down;
//  extraction only, no behavior change.
namespace {

///  Reads one integer field from the checked-in host/adapter live-state
///  capture catalog fixture, the same way
///  `adapter_ipc_connection_test.cpp`'s `ReadPrivateIpcLimit` reads the
///  private-IPC rate-limit fixture.
std::uint32_t ReadLiveStateToken(std::string_view key) {
    const std::string source = ReadSource(DOVAHLINK_LIVE_STATE_CATALOG_FIXTURE);
    const std::string marker = "\"" + std::string(key) + "\"";
    const std::size_t keyPosition = source.find(marker);
    REQUIRE(keyPosition != std::string::npos);
    const std::size_t colon = source.find(':', keyPosition + marker.size());
    REQUIRE(colon != std::string::npos);
    const std::size_t valuePosition =
        source.find_first_not_of(" \t\r\n", colon + 1);
    REQUIRE(valuePosition != std::string::npos);

    std::uint32_t value = 0;
    const auto [end, error] = std::from_chars(
        source.data() + valuePosition, source.data() + source.size(), value);
    REQUIRE(error == std::errc{});
    REQUIRE(end != source.data() + valuePosition);
    return value;
}

} //  namespace

TEST_CASE("Adapter live-state capture enums match the shared catalog fixture",
          "[capture][live-state]") {
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals) ==
          ReadLiveStateToken("characterVitals"));
    CHECK(static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp) ==
          ReadLiveStateToken("characterXp"));
    CHECK(static_cast<std::uint32_t>(
              CharacterSampleToken::kCharacterLevelBaseline) ==
          ReadLiveStateToken("characterLevelBaseline"));
    CHECK(static_cast<std::uint32_t>(
              CharacterEventKey::kCharacterLevelChanged) ==
          ReadLiveStateToken("characterLevelChanged"));
}
