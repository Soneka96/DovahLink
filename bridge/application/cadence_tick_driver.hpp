#pragma once

#include "application/cadence_scheduler.hpp"
#include "application/capture_dispatch_worker.hpp"
#include "application/constants.hpp"
#include "application/task_marshaller.hpp"

#include <chrono>
#include <condition_variable>
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
///  onto the game thread through the injected `ITaskMarshaller`, per
///  `ai/context/skse/architecture.md`'s "Production capture and lifecycle
///  composition". Only the due-key evaluation and any resulting capture
///  happen on the game thread; the interval timing itself runs on this
///  ordinary background thread.
///
///  For each key `ICadenceScheduler::DueKeys` reports, this hands off a
///  `CaptureWorkItem` to `ICaptureDispatchWorker` with an empty payload
///  builder: no state area is registered before a later phase, so
///  `DueKeys` never reports a real key in production and this path is
///  exercised only by tests. Building real per-key data belongs to whichever
///  phase registers a sampled domain and gives this class a real key to
///  build data for.
class CadenceTickDriver final : public ICadenceTickDriver {
  public:
    ///  Binds the driver to its scheduler, dispatch worker, and game-thread
    ///  marshaller.
    ///  @param scheduler Reports due sampled-capture keys.
    ///  @param worker Receives a work item for each due key.
    ///  @param taskMarshaller Marshals the due-key check onto the game
    ///  thread.
    ///  @param tickInterval Background timer interval; defaults to the
    ///  approved production value.
    CadenceTickDriver(ICadenceScheduler& scheduler,
                      ICaptureDispatchWorker& worker,
                      ITaskMarshaller& taskMarshaller,
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

    ///  Reports due sampled-capture keys.
    ICadenceScheduler& scheduler_;

    ///  Receives a work item for each due key.
    ICaptureDispatchWorker& worker_;

    ///  Marshals the due-key check onto the game thread.
    ITaskMarshaller& taskMarshaller_;

    ///  Background timer interval.
    std::chrono::milliseconds tickInterval_;

    ///  Synchronizes `stopping_` with `stopSignal_`.
    std::mutex mutex_;

    ///  Wakes the background thread early when `Stop` is signaled.
    std::condition_variable stopSignal_;

    ///  Set by `Stop`; causes the background thread to exit.
    bool stopping_ = false;

    ///  Background timer thread started by `Start`.
    std::thread thread_;
};

} //  namespace dovahlink::application
