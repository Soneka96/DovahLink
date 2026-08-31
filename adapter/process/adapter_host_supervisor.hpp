#pragma once

#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <optional>
#include <stop_token>
#include <thread>

namespace dovahlink::adapter::process {

class IAdapterHostRendezvousReader;
class IAdapterHostProcessLauncher;

} // namespace dovahlink::adapter::process

namespace dovahlink::adapter::ipc {
class IAdapterIpcConnection;
} // namespace dovahlink::adapter::ipc

namespace dovahlink::adapter::process {

///  Watches for the adapter's private IPC connection losing its host and
///  keeps rediscovering a target for the whole adapter process lifetime,
///  reconfiguring the existing long-lived connection's target in place -- it
///  never stops or restarts that connection. Not a one-shot startup step: a
///  host that crashes and rebinds a new dynamic port is rediscovered and
///  reconnected without adapter restart.
class IAdapterHostSupervisor {
public:
  virtual ~IAdapterHostSupervisor() = default;

  ///  Starts the supervisor's background discovery loop. Idempotent: a call
  ///  after it is already running is a no-op.
  virtual void Start() = 0;

  ///  Marks the supervisor stopping (no new discovery round begins),
  ///  interrupts any in-flight bounded wait, and joins the background
  ///  thread. Idempotent.
  virtual void RequestStop() = 0;

  ///  Notifies the supervisor that one target-generation connection attempt
  ///  has ended, so it decides whether to rediscover or launch a fresh host.
  ///  Every notification must identify the target generation that ended.
  virtual void NotifyConnectionLost(std::uint64_t targetGeneration,
                                    ipc::AdapterIpcAttemptOutcome outcome) = 0;
};

///  @copydoc IAdapterHostSupervisor
class AdapterHostSupervisor final : public IAdapterHostSupervisor {
public:
  ///  Creates a supervisor for one adapter process's whole discovery
  ///  lifetime.
  ///  @param reader Reads a candidate endpoint from the startup rendezvous.
  ///  @param launcher Launches a fresh packaged host when no candidate can
  ///  be adopted.
  ///  @param connection The existing long-lived private IPC connection,
  ///  started after a candidate target has been configured.
  ///  @param failedRoundBackoff The bound a discovery round without a usable
  ///  candidate waits before retrying.
  AdapterHostSupervisor(IAdapterHostRendezvousReader &reader,
                        IAdapterHostProcessLauncher &launcher,
                        ipc::IAdapterIpcConnection &connection,
                        std::chrono::milliseconds failedRoundBackoff =
                            kDefaultAdapterHostSupervisorFailedRoundBackoff);

  ///  Calls `RequestStop()` as a fallback so the background thread is never
  ///  leaked.
  ~AdapterHostSupervisor() override;

  AdapterHostSupervisor(const AdapterHostSupervisor &) = delete;
  AdapterHostSupervisor &operator=(const AdapterHostSupervisor &) = delete;

  ///  @copydoc IAdapterHostSupervisor::Start
  void Start() override;

  ///  @copydoc IAdapterHostSupervisor::RequestStop
  void RequestStop() override;

  ///  @copydoc IAdapterHostSupervisor::NotifyConnectionLost
  void NotifyConnectionLost(std::uint64_t targetGeneration,
                            ipc::AdapterIpcAttemptOutcome outcome) override;

private:
  ///  Runs on the background thread: repeatedly runs a discovery round and
  ///  waits for the next reason to run another, until stopped.
  void RunLoop();

  ///  Runs one bounded discovery round: select a rendezvous candidate, or
  ///  launch a fresh host when requested or when no candidate exists.
  ///  @return The selected candidate, or `std::nullopt` if none was produced.
  std::optional<AdapterHostEndpoint>
  RunOneDiscoveryRound(std::stop_token cancellationToken,
                       bool preferFreshLaunch);

  ///  Retargets the live connection to one complete endpoint-generation
  ///  snapshot and returns its generation.
  std::uint64_t ReconfigureLiveTarget(const AdapterHostEndpoint &endpoint);

  ///  Waits until either `NotifyConnectionLost` is called or a stop is
  ///  requested, whichever comes first.
  ///  @return the terminal attempt outcome, or `std::nullopt` if a stop was
  ///  requested.
  std::optional<ipc::AdapterIpcAttemptOutcome>
  WaitForConnectionLostOrStop(std::uint64_t targetGeneration);

  ///  Waits out `failedRoundBackoff_`, or returns immediately once a stop is
  ///  requested.
  ///  @return `false` if a stop was requested; `true` if the backoff simply
  ///  elapsed.
  bool WaitBackoffOrStop();

  ///  Reads a candidate endpoint from the startup rendezvous.
  IAdapterHostRendezvousReader &reader_;
  ///  Launches a fresh packaged host when no candidate can be adopted.
  IAdapterHostProcessLauncher &launcher_;
  ///  The existing long-lived private IPC connection, started after its
  ///  first target is configured.
  ipc::IAdapterIpcConnection &connection_;
  ///  The next supervisor-assigned target generation.
  std::uint64_t nextTargetGeneration_ = 0;
  ///  The bound a failed round waits before retrying.
  std::chrono::milliseconds failedRoundBackoff_;
  ///  Guards the worker thread object itself, separate from `stateMutex_` so
  ///  `Start`/`RequestStop` never contend with the condition variable's own
  ///  locking.
  std::mutex lifecycleMutex_;
  ///  Serializes a discovery/launch/target-start operation with shutdown's
  ///  final stop check. Reentrant `RequestStop()` from the worker is handled
  ///  without reacquiring this mutex.
  std::mutex operationMutex_;
  ///  The background discovery thread, started by `Start()`.
  std::thread worker_;
  ///  Guards `stopping_` and `connectionLost_`, and backs `stateCondition_`.
  std::mutex stateMutex_;
  ///  Signaled on `RequestStop()` and `NotifyConnectionLost()`.
  std::condition_variable stateCondition_;
  ///  Set by `RequestStop()`; checked before every round and wait.
  bool stopping_ = false;
  ///  Set by `NotifyConnectionLost()`; consumed by
  ///  `WaitForConnectionLostOrStop()` for the active target generation.
  bool connectionLost_ = false;
  ///  The target generation associated with `connectionLost_`.
  std::uint64_t completedTargetGeneration_ = 0;
  ///  The terminal outcome associated with `connectionLost_`.
  ipc::AdapterIpcAttemptOutcome completedOutcome_ =
      ipc::AdapterIpcAttemptOutcome::kDisconnected;
  ///  Cancels bounded discovery operations when shutdown begins.
  std::stop_source cancellationSource_;
};

} //  namespace dovahlink::adapter::process
