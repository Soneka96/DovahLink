#pragma once

#include "application/active_play_context_provider.hpp"
#include "application/cadence_scheduler.hpp"
#include "application/capture_dispatch_worker.hpp"
#include "application/constants.hpp"
#include "application/task_marshaller.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <memory>
#include <mutex>
#include <thread>

namespace dovahlink::application {

///  Controls the approved game-thread tick source for sampled capture.
class ICadenceTickDriver {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICadenceTickDriver() = default;

    ///  Starts the background timer thread. Repeated calls are not supported.
    virtual void Start() = 0;

    ///  Signals the background timer thread to stop without waiting for it
    ///  to exit. Safe to call before `Start`.
    virtual void Stop() = 0;

    ///  Waits until the background timer thread has exited. Safe to call
    ///  whether or not `Start` was ever called.
    virtual void Join() = 0;
};

///  Owns a dedicated background thread that wakes every `tickInterval`
///  (`kCadenceTickInterval` in production) and marshals one due-key check
///  onto the game thread through the injected `ITaskMarshaller`. Only the
///  due-key evaluation and any resulting capture happen on the game thread;
///  the interval timing itself runs on this ordinary background thread.
///
///  Each game-thread tick pins the active play context once, before
///  checking `ICadenceScheduler::DueKeys`, and reuses that same pinned
///  context for every due key in that tick -- the same atomic guard-then-pin
///  shape native-event capture uses -- skipping the whole due-key check when
///  no context is active. For each due key, this hands off a
///  `CaptureWorkItem` to `ICaptureDispatchWorker` with a closure that
///  reports no change: no state area is registered before a later phase, so
///  `DueKeys` never reports a real key in production and this path is
///  exercised only by tests. Building the real per-key apply-and-compare
///  closure belongs to whichever phase registers a sampled domain and gives
///  this class a real key to build data for.
class CadenceTickDriver final : public ICadenceTickDriver {
  public:
    ///  Binds the driver to its scheduler, dispatch worker, pinned-context
    ///  provider, and game-thread marshaller.
    ///  @param scheduler Reports due sampled-capture keys.
    ///  @param worker Receives a work item for each due key.
    ///  @param taskMarshaller Marshals the due-key check onto the game
    ///  thread.
    ///  @param activeContext Provides the play context each tick's due keys
    ///  are captured against.
    ///  @param tickInterval Background timer interval; defaults to the
    ///  approved production value.
    CadenceTickDriver(ICadenceScheduler& scheduler,
                      ICaptureDispatchWorker& worker,
                      ITaskMarshaller& taskMarshaller,
                      IActivePlayContextProvider& activeContext,
                      std::chrono::milliseconds tickInterval =
                          kCadenceTickInterval);

    ///  Stops and joins the background thread if it is still running.
    ~CadenceTickDriver() override;

    ///  Copying a driver is not supported.
    CadenceTickDriver(const CadenceTickDriver&) = delete;

    ///  Copy assignment is not supported.
    CadenceTickDriver& operator=(const CadenceTickDriver&) = delete;

    ///  @copydoc ICadenceTickDriver::Start
    void Start() override;

    ///  @copydoc ICadenceTickDriver::Stop
    void Stop() override;

    ///  @copydoc ICadenceTickDriver::Join
    void Join() override;

  private:
    ///  Runs on the background thread: wakes every `tickInterval_` and
    ///  marshals one due-key check onto the game thread, until `Stop` is
    ///  signaled.
    void TickerLoop();

    ///  Runs on the game thread, via `taskMarshaller_`: reports due keys and
    ///  hands each one to `worker_`.
    void OnGameThreadTick();

    ///  Shared callback lifetime state that can outlive this driver while a
    ///  task remains queued in the game-thread executor.
    struct CallbackState;

    ///  Runs one queued callback through the independently owned lifetime
    ///  state, invoking the driver only while it is still alive.
    static void RunQueuedTick(std::shared_ptr<CallbackState> state) noexcept;

    ///  Shared callback lifetime state that can outlive this driver while a
    ///  task remains queued in the game-thread executor.
    struct CallbackState {
        ///  Synchronizes callback admission, cancellation, and pending state.
        std::mutex mutex;

        ///  Wakes teardown after an admitted callback has returned.
        std::condition_variable changed;

        ///  Driver owner while destruction has not completed.
        CadenceTickDriver* owner = nullptr;

        ///  Prevents queued callbacks from entering the driver after Stop.
        bool cancelled = false;

        ///  Whether one game-thread tick is queued or executing.
        bool tickPending = false;

        ///  Number of callbacks currently executing driver code.
        std::size_t callbacksInFlight = 0;
    };

    ///  Reports due sampled-capture keys.
    ICadenceScheduler& scheduler_;

    ///  Receives a work item for each due key.
    ICaptureDispatchWorker& worker_;

    ///  Marshals the due-key check onto the game thread.
    ITaskMarshaller& taskMarshaller_;

    ///  Provides the play context each tick's due keys are captured against.
    IActivePlayContextProvider& activeContext_;

    ///  Background timer interval.
    std::chrono::milliseconds tickInterval_;

    ///  Synchronizes `stopping_` with `stopSignal_`.
    std::mutex mutex_;

    ///  Wakes the background thread early when `Stop` is signaled.
    std::condition_variable stopSignal_;

    ///  Set by `Stop`; causes the background thread to exit.
    bool stopping_ = false;

    ///  Set by `Start`; keeps a pre-start `Stop` call from disabling a later
    ///  start.
    bool started_ = false;

    ///  Background timer thread started by `Start`.
    std::thread thread_;

    ///  Lifetime state captured by queued game-thread callbacks.
    std::shared_ptr<CallbackState> callbackState_;
};

} //  namespace dovahlink::application
