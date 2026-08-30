#include "capture/adapter_capture_handoff_queue.hpp"

#include "capture/adapter_capture_constants.hpp"

#include <utility>

namespace dovahlink::adapter::capture {

AdapterCaptureHandoffQueue::AdapterCaptureHandoffQueue(
    std::function<void(const AdapterCaptureWorkItem &)> onDrained,
    std::function<void(const AdapterCaptureWorkItem &)> onRejected)
    : onDrained_(std::move(onDrained)), onRejected_(std::move(onRejected)) {
  worker_ = std::thread([this] { WorkerLoop(); });
}

AdapterCaptureHandoffQueue::~AdapterCaptureHandoffQueue() { Stop(); }

bool AdapterCaptureHandoffQueue::TryEnqueue(AdapterCaptureWorkItem item) {
  bool accepted = false;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!stopping_ && queue_.size() < kMaxAdapterCaptureQueueItems) {
      queue_.push_back(std::move(item));
      accepted = true;
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
    if (stopping_) {
      return;
    }
    stopping_ = true;
  }

  itemAvailable_.notify_all();
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
                          [this] { return stopping_ || !queue_.empty(); });
      if (queue_.empty()) {
        //  The wait predicate only admits an empty queue once `stopping_` is
        //  set, so there is nothing left to drain.
        return;
      }

      item = std::move(queue_.front());
      queue_.pop_front();
    }

    try {
      onDrained_(item);
    } catch (...) {
      //  A drain callback must never escape and terminate the worker thread.
    }
  }
}

} //  namespace dovahlink::adapter::capture
