#pragma once

#include "application/active_play_context_provider.hpp"
#include "application/capture_queue_diagnostics.hpp"
#include "application/capture_work_item.hpp"
#include "application/state_publisher.hpp"

#include <condition_variable>
#include <deque>
#include <mutex>
#include <thread>

namespace dovahlink::application {

///  Owns the boundary between a game-thread capture and worker-side
///  publication building, and is itself the single per-state-area ordering
///  point: `TryEnqueue` copies an already-validated owned `CaptureWorkItem`
///  -- pinned to the play context it was captured against -- into a bounded
///  queue without blocking its caller, and a dedicated worker thread
///  dequeues each item and, for one still-current item at a time, applies
///  its captured value, determines whether it changed, and hands the result
///  to `IStatePublisher::PublishCapture` to assign the authoritative
///  revision and publish -- all through this one path, regardless of
///  whether the item arrived from a native-event or sampled-capture source.
///  An item whose pinned context is no longer the active one is discarded
///  before either step runs. No ordering logic exists in this class beyond
///  that and FIFO dequeue order.
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

    ///  Enqueues one item without blocking the calling thread. A rejection
    ///  because the queue is already at capacity is reported through the
    ///  injected `ICaptureQueueDiagnostics`; a rejection because `Stop` has
    ///  already been called is not, since it is an expected shutdown
    ///  outcome rather than a capacity loss worth flagging.
    ///  @param item Owned capture work item.
    ///  @return `true` when enqueued; `false` when the queue is already at
    ///  `kMaxCaptureQueueItems` capacity or `Stop` has already been called.
    [[nodiscard]] virtual bool TryEnqueue(CaptureWorkItem item) = 0;
};

///  @copydoc ICaptureDispatchWorker
class CaptureDispatchWorker final : public ICaptureDispatchWorker {
  public:
    ///  Binds the worker to the publisher its dequeued items are dispatched
    ///  to, the provider used to detect a stale pinned context, and the
    ///  diagnostics sink capacity rejections are reported through.
    ///  @param publisher Receives every dispatched item's built publication.
    ///  @param activeContext Read to check whether a dequeued item's pinned
    ///  context is still the active one, both before dispatching it and
    ///  again, as the final admission check, immediately before the built
    ///  publication reaches the sink.
    ///  @param diagnostics Receives a signal when `TryEnqueue` rejects an
    ///  item for capacity.
    CaptureDispatchWorker(IStatePublisher& publisher,
                          IActivePlayContextProvider& activeContext,
                          ICaptureQueueDiagnostics& diagnostics);

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

    ///  Discards `item` if its pinned context is already stale; otherwise
    ///  applies its captured value against that context and, if changed,
    ///  publishes it through `publisher_`.
    ///  @param item Item to dispatch; already removed from the queue.
    void Dispatch(CaptureWorkItem item);

    ///  Receives every dispatched item's built publication.
    IStatePublisher& publisher_;

    ///  Read to detect a stale pinned context, both before dispatching an
    ///  item and as the final admission check immediately before publish.
    IActivePlayContextProvider& activeContext_;

    ///  Receives a signal when `TryEnqueue` rejects an item for capacity.
    ICaptureQueueDiagnostics& diagnostics_;

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
