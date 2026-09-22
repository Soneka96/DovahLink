#include "capture/adapter_capture_handoff_queue.hpp"

#include "constants.hpp"

#include <utility>

namespace dovahlink::adapter::capture {

AdapterCaptureHandoffQueue::AdapterCaptureHandoffQueue(
    std::function<void(const AdapterCaptureWorkItem&)> onDrained,
    std::function<void(const AdapterCaptureWorkItem&)> onRejected,
    int enqueueLockAttempts, bool yieldBetweenEnqueueLockAttempts)
    : onDrained_(std::move(onDrained)),
      onRejected_(std::move(onRejected)),
      enqueueLockAttempts_(enqueueLockAttempts),
      yieldBetweenEnqueueLockAttempts_(yieldBetweenEnqueueLockAttempts) {
    worker_ = std::thread([this] { WorkerLoop(); });
    workerThreadId_ = worker_.get_id();
}

AdapterCaptureHandoffQueue::~AdapterCaptureHandoffQueue() { Stop(); }

bool AdapterCaptureHandoffQueue::TryEnqueue(AdapterCaptureWorkItem item) {
    bool accepted = false;
    //  A handful of non-blocking attempts, never sleeping or waiting. By
    //  default (production: `yieldBetweenEnqueueLockAttempts_` false) this
    //  call also never voluntarily yields between them. The worker thread's
    //  own critical section is intentionally extremely short -- a few
    //  instructions to remove one item, no I/O, no allocation -- so a
    //  handful of immediate retries can absorb ordinary, brief concurrent
    //  contention with it (for example three baseline samples enqueued back
    //  to back during resynchronization) without ever surrendering this
    //  call's own scheduler timeslice the way `std::this_thread::yield()`
    //  would, for a scheduler-dependent duration this Skyrim game-thread
    //  callback must not risk. Admission stays deliberately bounded and
    //  non-blocking, so a contention-only rejection remains possible if the
    //  worker is preempted mid-critical-section; that tradeoff is preferred
    //  over letting this call wait for the scheduler. A genuinely full or
    //  stopped queue still rejects immediately, on the very first attempt
    //  that acquires the mutex.
    for (int attempt = 0; attempt < enqueueLockAttempts_; ++attempt) {
        std::unique_lock<std::mutex> lock(mutex_, std::try_to_lock);
        if (lock.owns_lock()) {
            if (!stopping_ && count_ < buffer_.size()) {
                buffer_[(head_ + count_) % buffer_.size()] = std::move(item);
                ++count_;
                accepted = true;
            }
            break;
        }
        if (yieldBetweenEnqueueLockAttempts_) {
            std::this_thread::yield();
        }
    }

    if (accepted) {
        itemAvailable_.notify_one();
        return true;
    }

    try {
        onRejected_(item);
    } catch (...) {
        //  A diagnostics callback must never escape into the caller, which may be
        //  the Skyrim game thread.
    }
    return false;
}

void AdapterCaptureHandoffQueue::Stop() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stopping_ = true;
    }

    itemAvailable_.notify_all();
    if (std::this_thread::get_id() == workerThreadId_) {
        return;
    }

    std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);
    if (worker_.joinable()) {
        worker_.join();
    }
}

void AdapterCaptureHandoffQueue::WorkerLoop() {
    while (true) {
        AdapterCaptureWorkItem item;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            itemAvailable_.wait(lock,
                                [this] { return stopping_ || count_ > 0; });
            if (count_ == 0) {
                //  The wait predicate only admits an empty queue once `stopping_` is
                //  set, so there is nothing left to drain.
                return;
            }

            item = std::move(buffer_[head_]);
            head_ = (head_ + 1) % buffer_.size();
            --count_;
        }

        try {
            onDrained_(item);
        } catch (...) {
            //  A drain callback must never escape and terminate the worker thread.
        }
    }
}

} //  namespace dovahlink::adapter::capture
