#include "process/adapter_host_supervisor.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"

namespace dovahlink::adapter::process {

AdapterHostSupervisor::AdapterHostSupervisor(
    IAdapterHostRendezvousReader &reader, IAdapterHostProcessLauncher &launcher,
    ipc::IAdapterIpcConnection &connection,
    std::chrono::milliseconds failedRoundBackoff)
    : reader_(reader), launcher_(launcher), connection_(connection),
      failedRoundBackoff_(failedRoundBackoff) {}

AdapterHostSupervisor::~AdapterHostSupervisor() { RequestStop(); }

void AdapterHostSupervisor::Start() {
  std::lock_guard<std::mutex> lock(lifecycleMutex_);
  if (worker_.joinable()) {
    return;
  }
  worker_ = std::thread([this] { RunLoop(); });
}

void AdapterHostSupervisor::RequestStop() {
  cancellationSource_.request_stop();
  const bool calledFromWorker = [&] {
    std::lock_guard<std::mutex> lock(lifecycleMutex_);
    return worker_.joinable() && worker_.get_id() == std::this_thread::get_id();
  }();
  if (!calledFromWorker) {
    std::lock_guard<std::mutex> operationLock(operationMutex_);
    std::lock_guard<std::mutex> stateLock(stateMutex_);
    stopping_ = true;
  } else {
    std::lock_guard<std::mutex> stateLock(stateMutex_);
    stopping_ = true;
  }
  stateCondition_.notify_all();

  std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);
  if (worker_.joinable() && worker_.get_id() != std::this_thread::get_id()) {
    worker_.join();
  }
}

void AdapterHostSupervisor::NotifyConnectionLost(
    std::uint64_t targetGeneration, ipc::AdapterIpcAttemptOutcome outcome) {
  {
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (stopping_) {
      return;
    }
    //  Only the currently configured target generation can complete the
    //  supervisor's active wait. Unknown future generations and stale older
    //  generations are ignored rather than poisoning that wait.
    if (targetGeneration != 0 && targetGeneration != nextTargetGeneration_) {
      return;
    }
    connectionLost_ = true;
    completedTargetGeneration_ = targetGeneration;
    completedOutcome_ = outcome;
  }
  stateCondition_.notify_all();
}

void AdapterHostSupervisor::RunLoop() {
  std::stop_token cancellationToken = cancellationSource_.get_token();
  bool preferFreshLaunch = false;
  while (true) {
    {
      std::lock_guard<std::mutex> lock(stateMutex_);
      if (stopping_) {
        return;
      }
    }

    bool succeeded = false;
    bool needsRecoveryBackoff = false;
    try {
      std::optional<AdapterHostEndpoint> endpoint;
      std::uint64_t targetGeneration = 0;
      {
        std::lock_guard<std::mutex> operationLock(operationMutex_);
        {
          std::lock_guard<std::mutex> lock(stateMutex_);
          if (stopping_ || cancellationToken.stop_requested()) {
            return;
          }
        }
        endpoint = RunOneDiscoveryRound(cancellationToken, preferFreshLaunch);
        if (endpoint.has_value()) {
          {
            std::lock_guard<std::mutex> lock(stateMutex_);
            if (stopping_ || cancellationToken.stop_requested()) {
              return;
            }
          }
          targetGeneration = ReconfigureLiveTarget(*endpoint);
          connection_.Start();
        }
      }
      if (endpoint.has_value()) {
        std::optional<ipc::AdapterIpcAttemptOutcome> outcome =
            WaitForConnectionLostOrStop(targetGeneration);
        if (!outcome.has_value()) {
          return;
        }
        preferFreshLaunch =
            *outcome == ipc::AdapterIpcAttemptOutcome::kConnectFailed ||
            *outcome == ipc::AdapterIpcAttemptOutcome::kAuthenticationFailed;
        //  An authenticated connection that later disconnects still needs a
        //  bounded recovery delay before the next round, the same as a
        //  failed connect/authentication attempt: otherwise a peer that
        //  keeps connecting and disconnecting (for example by repeatedly
        //  tripping the inbound rate limit) causes a tight reconnect loop.
        needsRecoveryBackoff =
            *outcome == ipc::AdapterIpcAttemptOutcome::kDisconnected;
        succeeded = true;
      }
    } catch (...) {
      //  No collaborator failure may escape this background thread; treat it
      //  as a failed round, per the worker-thread exception-containment rule.
      succeeded = false;
      preferFreshLaunch = true;
    }

    if (!succeeded || preferFreshLaunch || needsRecoveryBackoff) {
      if (!WaitBackoffOrStop()) {
        return;
      }
    }
  }
}

std::optional<AdapterHostEndpoint>
AdapterHostSupervisor::RunOneDiscoveryRound(std::stop_token cancellationToken,
                                            bool preferFreshLaunch) {
  if (!preferFreshLaunch) {
    std::optional<AdapterHostEndpoint> candidate = reader_.TryRead();
    if (cancellationToken.stop_requested()) {
      return std::nullopt;
    }
    if (candidate.has_value()) {
      return candidate;
    }
  }

  if (cancellationToken.stop_requested()) {
    return std::nullopt;
  }
  return launcher_.Launch(cancellationToken);
}

std::uint64_t AdapterHostSupervisor::ReconfigureLiveTarget(
    const AdapterHostEndpoint &endpoint) {
  std::uint64_t targetGeneration;
  {
    std::lock_guard<std::mutex> lock(stateMutex_);
    targetGeneration = ++nextTargetGeneration_;
  }
  connection_.ConfigureTarget(ipc::AdapterIpcTarget{
      .port = endpoint.port,
      .proofToken = endpoint.proofToken,
      .hostProofKey = endpoint.hostProofKey,
      .targetGeneration = targetGeneration,
  });
  return targetGeneration;
}

std::optional<ipc::AdapterIpcAttemptOutcome>
AdapterHostSupervisor::WaitForConnectionLostOrStop(
    std::uint64_t targetGeneration) {
  std::unique_lock<std::mutex> lock(stateMutex_);
  stateCondition_.wait(lock, [this, targetGeneration] {
    return stopping_ ||
           (connectionLost_ && completedTargetGeneration_ == targetGeneration);
  });
  if (stopping_) {
    return std::nullopt;
  }
  connectionLost_ = false;
  completedTargetGeneration_ = 0;
  return completedOutcome_;
}

bool AdapterHostSupervisor::WaitBackoffOrStop() {
  std::unique_lock<std::mutex> lock(stateMutex_);
  stateCondition_.wait_for(lock, failedRoundBackoff_,
                           [this] { return stopping_; });
  return !stopping_;
}

} //  namespace dovahlink::adapter::process
