#pragma once

#include "application/capture_work_item.hpp"
#include "application/state_publisher.hpp"

#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>

namespace dovahlink::application {

///  Owns the boundary between a game-thread capture and worker-side
///  publication building: `TryEnqueue` copies an already-validated owned
///  `CaptureWorkItem` into a bounded queue without blocking its caller, and a
///  dedicated worker thread dequeues each item and calls the injected
///  `IStatePublisher`, which is the single per-state-area ordering point for
///  applying a captured value, determining change, and assigning the
///  authoritative revision (`ai/context/skse/architecture.md`'s "Production
///  capture and lifecycle composition"). No ordering logic exists in this
///  class beyond FIFO dequeue order.
class ICaptureDispatchWorker {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICaptureDispatchWorker() = default;

    ///  Starts the worker thread. Repeated calls are not supported.
    virtual void Start() = 0;

    ///  Signals the worker thread to stop and discards any not-yet-dispatched
    ///  queued items without waiting for the thread to exit.
    virtual void Stop() = 0;

    ///  Waits until the worker thread has exited. Safe to call whether or not
    ///  `Start` was ever called.
    virtual void Join() = 0;

    ///  Enqueues one item without blocking the calling thread.
    ///  @param item Owned capture work item.
    ///  @return `true` when enqueued; `false` when the queue is already at
    ///  `kMaxCaptureQueueItems` capacity or `Stop` has already been called.
    [[nodiscard]] virtual bool TryEnqueue(CaptureWorkItem item) = 0;
};

///  @copydoc ICaptureDispatchWorker
class CaptureDispatchWorker final : public ICaptureDispatchWorker {
  public:
    ///  Binds the worker to the publisher its dequeued items are dispatched
    ///  to.
    ///  @param publisher Receives every dispatched item's built publication.
    explicit CaptureDispatchWorker(IStatePublisher& publisher);

    ///  Stops and joins the worker thread if it is still running.
    ~CaptureDispatchWorker() override;

    ///  Copying a worker is not supported.
    CaptureDispatchWorker(const CaptureDispatchWorker&) = delete;

    ///  Copy assignment is not supported.
    CaptureDispatchWorker& operator=(const CaptureDispatchWorker&) = delete;

    ///  @copydoc ICaptureDispatchWorker::Start
    void Start() override;

    ///  @copydoc ICaptureDispatchWorker::Stop
    void Stop() override;

    ///  @copydoc ICaptureDispatchWorker::Join
    void Join() override;

    ///  @copydoc ICaptureDispatchWorker::TryEnqueue
    [[nodiscard]] bool TryEnqueue(CaptureWorkItem item) override;

  private:
    ///  Runs on the worker thread: dequeues items in FIFO order and
    ///  dispatches each until `Stop` is signaled.
    void WorkerLoop();

    ///  Calls `publisher_`'s matching method for one dequeued item.
    ///  @param item Item to dispatch; already removed from the queue.
    void Dispatch(CaptureWorkItem item);

    ///  Receives every dispatched item's built publication.
    IStatePublisher& publisher_;

    ///  Synchronizes `queue_` and `stopping_`.
    std::mutex mutex_;

    ///  Wakes the worker thread when an item is enqueued or `Stop` is
    ///  signaled.
    std::condition_variable itemAvailable_;

    ///  Items awaiting dispatch, in FIFO order.
    std::deque<CaptureWorkItem> queue_;

    ///  Set by `Stop`; causes the worker thread to exit and `TryEnqueue` to
    ///  reject further items.
    bool stopping_ = false;

    ///  Worker thread started by `Start`.
    std::thread thread_;
};

} //  namespace dovahlink::application
