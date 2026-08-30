#include "process/adapter_host_supervisor.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_handshake_verifier.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"

namespace dovahlink::adapter::process {

AdapterHostSupervisor::AdapterHostSupervisor(
    IAdapterHostRendezvousReader &reader,
    IAdapterHostHandshakeVerifier &verifier,
    ipc::WinsockAdapterIpcSocket &verifierSocket,
    IAdapterHostProcessLauncher &launcher,
    ipc::WinsockAdapterIpcSocket &connectionSocket,
    ipc::SettableAdapterIpcPeerProofProvider &connectionProofProvider,
    ipc::IAdapterIpcConnection &connection,
    std::chrono::milliseconds failedRoundBackoff)
    : reader_(reader), verifier_(verifier), verifierSocket_(verifierSocket),
      launcher_(launcher), connectionSocket_(connectionSocket),
      connectionProofProvider_(connectionProofProvider),
      connection_(connection), failedRoundBackoff_(failedRoundBackoff) {}

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
  verifierSocket_.RequestStop();
  {
    std::lock_guard<std::mutex> lock(stateMutex_);
    stopping_ = true;
  }
  stateCondition_.notify_all();

  std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);
  if (worker_.joinable() && worker_.get_id() != std::this_thread::get_id()) {
    worker_.join();
  }
}

void AdapterHostSupervisor::NotifyConnectionLost() {
  {
    std::lock_guard<std::mutex> lock(stateMutex_);
    connectionLost_ = true;
  }
  stateCondition_.notify_all();
}

void AdapterHostSupervisor::RunLoop() {
  std::stop_token cancellationToken = cancellationSource_.get_token();
  while (true) {
    {
      std::lock_guard<std::mutex> lock(stateMutex_);
      if (stopping_) {
        return;
      }
    }

    bool succeeded = false;
    try {
      std::optional<AdapterHostEndpoint> endpoint =
          RunOneDiscoveryRound(cancellationToken);
      if (endpoint.has_value()) {
        std::lock_guard<std::mutex> lock(stateMutex_);
        if (stopping_ || cancellationToken.stop_requested()) {
          return;
        }
        ReconfigureLiveTarget(*endpoint);
        connection_.Start();
        succeeded = true;
      }
    } catch (...) {
      //  No collaborator failure may escape this background thread; treat
      //  it as a failed round, per ai/context/skse/cpp-style.md's
      //  worker-thread exception-containment rule -- host failure must
      //  never crash the adapter.
      succeeded = false;
    }

    if (succeeded) {
      if (!WaitForConnectionLostOrStop()) {
        return;
      }
    } else {
      if (!WaitBackoffOrStop()) {
        return;
      }
    }
  }
}

std::optional<AdapterHostEndpoint>
AdapterHostSupervisor::RunOneDiscoveryRound(std::stop_token cancellationToken) {
  std::optional<AdapterHostEndpoint> candidate = reader_.TryRead();
  if (cancellationToken.stop_requested()) {
    return std::nullopt;
  }
  if (!cancellationToken.stop_requested() && candidate.has_value() &&
      VerifyCandidate(*candidate, cancellationToken)) {
    return candidate;
  }

  {
    //  A stop requested while adoption was in flight must still prevent a
    //  fresh launch: no host relaunch is ever initiated once shutdown has
    //  begun, even mid-round.
    std::lock_guard<std::mutex> lock(stateMutex_);
    if (stopping_ || cancellationToken.stop_requested()) {
      return std::nullopt;
    }
  }

  candidate = launcher_.Launch(cancellationToken);
  if (cancellationToken.stop_requested()) {
    return std::nullopt;
  }
  if (!cancellationToken.stop_requested() && candidate.has_value() &&
      VerifyCandidate(*candidate, cancellationToken)) {
    return candidate;
  }

  return std::nullopt;
}

bool AdapterHostSupervisor::VerifyCandidate(
    const AdapterHostEndpoint &candidate, std::stop_token cancellationToken) {
  if (cancellationToken.stop_requested()) {
    return false;
  }
  verifierSocket_.SetPort(candidate.port);
  return verifier_.Verify(candidate, cancellationToken);
}

void AdapterHostSupervisor::ReconfigureLiveTarget(
    const AdapterHostEndpoint &endpoint) {
  connectionSocket_.SetPort(endpoint.port);
  connectionProofProvider_.SetToken(endpoint.proofToken);
}

bool AdapterHostSupervisor::WaitForConnectionLostOrStop() {
  std::unique_lock<std::mutex> lock(stateMutex_);
  stateCondition_.wait(lock, [this] { return stopping_ || connectionLost_; });
  connectionLost_ = false;
  return !stopping_;
}

bool AdapterHostSupervisor::WaitBackoffOrStop() {
  std::unique_lock<std::mutex> lock(stateMutex_);
  stateCondition_.wait_for(lock, failedRoundBackoff_,
                           [this] { return stopping_; });
  return !stopping_;
}

} //  namespace dovahlink::adapter::process
