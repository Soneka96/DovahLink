#pragma once

#include <array>
#include <condition_variable>
#include <cstddef>
#include <functional>
#include <mutex>
#include <thread>

#include "capture/adapter_capture_work_item.hpp"
#include "constants.hpp"

namespace dovahlink::adapter::capture {

///  Owns the bounded, non-blocking handoff from a Skyrim game-thread callback
///  to worker-owned code. A game-thread caller only ever calls `TryEnqueue`,
///  which never blocks and never touches Skyrim state itself; a dedicated
///  worker thread drains accepted items in FIFO order and invokes the
///  constructor-injected drain callback. Carries no per-key policy of its
///  own -- every item is a single generic work-item shape.
class IAdapterCaptureHandoffQueue {
  public:
    virtual ~IAdapterCaptureHandoffQueue() = default;

    ///  Attempts to enqueue a captured value. Never blocks: at capacity or after
    ///  `Stop()`, the item is rejected rather than waited for.
    ///  @return `true` when the item was accepted onto the queue.
    virtual bool TryEnqueue(AdapterCaptureWorkItem item) = 0;

    ///  Stops accepting new items, wakes the worker thread, and waits for it to
    ///  drain every already-accepted item before returning. When called from the
    ///  drain callback on the worker thread, it marks the queue stopped and
    ///  returns without self-joining; an external owner must later join it.
    ///  Idempotent: repeated calls remain safe.
    virtual void Stop() = 0;
};

///  @copydoc IAdapterCaptureHandoffQueue
class AdapterCaptureHandoffQueue final : public IAdapterCaptureHandoffQueue {
  public:
    ///  Creates the queue and starts its drain thread immediately.
    ///  @param onDrained Invoked on the worker thread for each item drained in
    ///  FIFO order. An exception thrown by this callback is contained; it never
    ///  escapes the worker thread.
    ///  @param onRejected Invoked on the caller's thread -- which may be the
    ///  Skyrim game thread -- when `TryEnqueue` rejects an item. An exception
    ///  thrown by this callback is contained; it never escapes `TryEnqueue`.
    AdapterCaptureHandoffQueue(
        std::function<void(const AdapterCaptureWorkItem&)> onDrained,
        std::function<void(const AdapterCaptureWorkItem&)> onRejected);

    ///  Calls `Stop()` as a fallback so the worker thread is never leaked.
    ~AdapterCaptureHandoffQueue() override;

    AdapterCaptureHandoffQueue(const AdapterCaptureHandoffQueue&) = delete;
    AdapterCaptureHandoffQueue&
    operator=(const AdapterCaptureHandoffQueue&) = delete;

    ///  @copydoc IAdapterCaptureHandoffQueue::TryEnqueue
    bool TryEnqueue(AdapterCaptureWorkItem item) override;

    ///  @copydoc IAdapterCaptureHandoffQueue::Stop
    void Stop() override;

  private:
    ///  Drains `queue_` in FIFO order until stopped and empty.
    void WorkerLoop();

    ///  Invoked on the worker thread for each drained item.
    std::function<void(const AdapterCaptureWorkItem&)> onDrained_;
    ///  Invoked on the caller's thread when an item is rejected.
    std::function<void(const AdapterCaptureWorkItem&)> onRejected_;
    ///  Guards `buffer_`, `head_`, `count_`, and `stopping_`.
    std::mutex mutex_;
    ///  Guards access to `worker_` during shutdown.
    std::mutex lifecycleMutex_;
    ///  Signaled when an item is enqueued or the queue is stopped.
    std::condition_variable itemAvailable_;
    ///  A preallocated, fixed-capacity ring buffer of accepted,
    ///  not-yet-drained items -- capacity `kMaxAdapterCaptureQueueItems`,
    ///  never grown or reallocated after construction. `head_` is the oldest
    ///  present item's slot; `count_` items are present starting there,
    ///  wrapping modulo `buffer_.size()`.
    std::array<AdapterCaptureWorkItem, kMaxAdapterCaptureQueueItems> buffer_{};
    ///  The oldest present item's slot in `buffer_`.
    std::size_t head_ = 0;
    ///  How many items are currently present in `buffer_`.
    std::size_t count_ = 0;
    ///  Whether `Stop()` has been called.
    bool stopping_ = false;
    ///  The dedicated drain thread.
    std::thread worker_;
    ///  The dedicated drain thread's immutable identity for self-stop checks.
    std::thread::id workerThreadId_;
};

} //  namespace dovahlink::adapter::capture
