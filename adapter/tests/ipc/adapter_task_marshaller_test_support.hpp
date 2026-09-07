#pragma once

#include "runtime/adapter_task_marshaller.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <functional>
#include <mutex>
#include <stdexcept>
#include <utility>
#include <vector>

//  Test-only helpers shared across adapter/ipc/ test files.
namespace dovahlink::adapter::ipc::test_support {

///  A fake `IAdapterTaskMarshaller` that stores tasks instead of running
///  them, so tests control exactly when marshaled work executes. Thread-safe:
///  `RunOnGameThread` may be called from a genuine background thread (for
///  example `AdapterIpcSession`'s detached trust-admin timeout worker), not
///  only from a test's own main thread.
class FakeAdapterTaskMarshaller final
    : public dovahlink::adapter::runtime::IAdapterTaskMarshaller {
public:
  void RunOnGameThread(std::function<void()> task) override {
    std::lock_guard<std::mutex> lock(mutex_);
    if (throwOnNextSchedule_) {
      throwOnNextSchedule_ = false;
      throw std::runtime_error("RunOnGameThread failed");
    }
    pendingTasks_.push_back(std::move(task));
    scheduled_.notify_all();
  }

  ///  Makes the next `RunOnGameThread` call throw instead of admitting its
  ///  task, so a test can prove a scheduling failure never leaks the
  ///  caller's pending-dispatch slot. Consumed by the call it affects; a
  ///  later `RunOnGameThread` call schedules normally again.
  void ThrowOnNextSchedule() {
    std::lock_guard<std::mutex> lock(mutex_);
    throwOnNextSchedule_ = true;
  }

  ///  The number of tasks not yet run.
  std::size_t PendingCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return pendingTasks_.size();
  }

  ///  Runs every currently pending task, in order, and keeps draining any
  ///  further task a running task itself schedules (for example a
  ///  reentrant `SendTrustAdminRequest` call resolved from inside another
  ///  request's own callback) until none remain. Tasks run outside the
  ///  lock, so a task that schedules another `RunOnGameThread` call does
  ///  not deadlock.
  void RunAllPending() {
    for (;;) {
      std::vector<std::function<void()>> tasks;
      {
        std::lock_guard<std::mutex> lock(mutex_);
        if (pendingTasks_.empty()) {
          return;
        }
        std::swap(tasks, pendingTasks_);
      }
      for (auto &task : tasks) {
        task();
      }
    }
  }

  ///  Blocks until at least one task has been scheduled -- for example by a
  ///  trust-admin request's detached timeout worker, running on a genuine
  ///  background thread with no other deterministic completion signal --
  ///  then behaves as `RunAllPending`. Fails the calling test rather than
  ///  hanging forever if `timeout` elapses first, so a genuine regression
  ///  surfaces as a failure instead of a stuck test run.
  ///  @param timeout How long to wait for the first task to be scheduled.
  ///  @throws std::runtime_error No task was scheduled within `timeout`.
  void WaitForPendingAndRunAll(
      std::chrono::milliseconds timeout = std::chrono::seconds(5)) {
    {
      std::unique_lock<std::mutex> lock(mutex_);
      bool scheduled = scheduled_.wait_for(
          lock, timeout, [this] { return !pendingTasks_.empty(); });
      if (!scheduled) {
        throw std::runtime_error(
            "WaitForPendingAndRunAll: no task scheduled within the timeout");
      }
    }
    RunAllPending();
  }

  ///  Runs exactly the single oldest still-pending task, in FIFO order,
  ///  without draining any other currently pending or newly scheduled task.
  ///  Used to test interleavings where a later-queued task's observable
  ///  outcome depends on exactly one earlier task having run first, and
  ///  `RunAllPending`'s drain-to-completion behavior would run both.
  void RunNextPending() {
    std::function<void()> task;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (pendingTasks_.empty()) {
        return;
      }
      task = std::move(pendingTasks_.front());
      pendingTasks_.erase(pendingTasks_.begin());
    }
    task();
  }

  ///  Blocks until at least `minCount` tasks have been scheduled, without
  ///  running any of them -- unlike `WaitForPendingAndRunAll`, which drains
  ///  as soon as it observes the first one. Used to prove a property holds
  ///  before any queued completion has run (for example that a resolved
  ///  trust-admin request's capacity slot is already released). Fails the
  ///  calling test rather than hanging forever if `timeout` elapses first.
  ///  @throws std::runtime_error Fewer than `minCount` tasks were scheduled
  ///  within `timeout`.
  void WaitForPendingCountAtLeast(
      std::size_t minCount,
      std::chrono::milliseconds timeout = std::chrono::seconds(5)) {
    std::unique_lock<std::mutex> lock(mutex_);
    bool reached = scheduled_.wait_for(lock, timeout, [this, minCount] {
      return pendingTasks_.size() >= minCount;
    });
    if (!reached) {
      throw std::runtime_error("WaitForPendingCountAtLeast: fewer than "
                               "minCount tasks scheduled within the timeout");
    }
  }

private:
  mutable std::mutex mutex_;
  ///  Notified after a task is pushed onto `pendingTasks_`. Guarded by
  ///  `mutex_`.
  std::condition_variable scheduled_;
  ///  Guarded by `mutex_`.
  std::vector<std::function<void()>> pendingTasks_;
  ///  Whether the next `RunOnGameThread` call should throw instead of
  ///  admitting its task. Guarded by `mutex_`.
  bool throwOnNextSchedule_ = false;
};

} //  namespace dovahlink::adapter::ipc::test_support
