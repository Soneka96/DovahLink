#include "application/capture_dispatch_worker.hpp"

#include "application/constants.hpp"

#include <utility>

namespace dovahlink::application {

CaptureDispatchWorker::CaptureDispatchWorker(IStatePublisher& publisher,
                                             IActivePlayContextProvider& activeContext,
                                             ICaptureQueueDiagnostics& diagnostics)
    : publisher_(publisher), activeContext_(activeContext),
      diagnostics_(diagnostics) {}

CaptureDispatchWorker::~CaptureDispatchWorker() {
    Stop();
    Join();
}

void CaptureDispatchWorker::Start() {
    thread_ = std::thread([this] { WorkerLoop(); });
}

void CaptureDispatchWorker::Stop() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stopping_ = true;
        queue_.clear();
    }
    itemAvailable_.notify_all();
}

void CaptureDispatchWorker::Join() {
    if (thread_.joinable()) {
        thread_.join();
    }
}

bool CaptureDispatchWorker::TryEnqueue(CaptureWorkItem item) {
    bool rejectedForCapacity = false;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (stopping_) {
            return false;
        }
        if (queue_.size() >= kMaxCaptureQueueItems) {
            rejectedForCapacity = true;
        } else {
            queue_.push_back(std::move(item));
        }
    }
    if (rejectedForCapacity) {
        //  Reported outside mutex_ so a diagnostics call can never delay
        //  the worker thread's own acquisition of the same lock.
        diagnostics_.RecordCaptureRejected(item.stateArea, item.mode);
        return false;
    }
    itemAvailable_.notify_one();
    return true;
}

void CaptureDispatchWorker::WorkerLoop() {
    while (true) {
        CaptureWorkItem item;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            itemAvailable_.wait(
                lock, [this] { return stopping_ || !queue_.empty(); });
            if (stopping_) {
                return;
            }
            item = std::move(queue_.front());
            queue_.pop_front();
        }
        try {
            Dispatch(std::move(item));
        } catch (...) {
            //  Per ai/context/skse/cpp-style.md's "Ownership and lifetime":
            //  never allow an exception to escape a worker-thread boundary.
            //  One failed item does not stop the worker from dispatching
            //  the next one.
        }
    }
}

void CaptureDispatchWorker::Dispatch(CaptureWorkItem item) {
    if (activeContext_.CurrentPlayContext() != item.playContext) {
        //  Already stale before any work started: the pinned context has
        //  been replaced since this item was captured. Discarded, not
        //  treated as an error.
        return;
    }

    auto data = item.applyAndBuildIfChanged(*item.playContext);
    if (!data.has_value()) {
        return;
    }

    auto expectedContext = item.playContext;
    auto stillCurrent = [this, expectedContext] {
        return activeContext_.CurrentPlayContext() == expectedContext;
    };
    publisher_.PublishCapture(item.stateArea, item.playContext->id,
                              item.playContext->revisions, item.mode,
                              std::move(*data), item.occurredAt, stillCurrent);
}

} //  namespace dovahlink::application
