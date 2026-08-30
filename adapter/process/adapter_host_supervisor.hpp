#pragma once

#include "ipc/settable_adapter_ipc_peer_proof_provider.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <chrono>
#include <condition_variable>
#include <mutex>
#include <optional>
#include <stop_token>
#include <thread>

namespace dovahlink::adapter::process {

class IAdapterHostRendezvousReader;
class IAdapterHostHandshakeVerifier;
class IAdapterHostProcessLauncher;

} // namespace dovahlink::adapter::process

namespace dovahlink::adapter::ipc {
class IAdapterIpcConnection;
} // namespace dovahlink::adapter::ipc

namespace dovahlink::adapter::process {

///  Watches for the adapter's private IPC connection losing its host and
///  keeps rediscovering and reverifying one for the whole adapter process
///  lifetime, reconfiguring the existing long-lived connection's target in
///  place -- it never stops or restarts that connection. Not a one-shot
///  startup step: a host that crashes and rebinds a new dynamic port is
///  rediscovered and reconnected without adapter restart.
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

  ///  Notifies the supervisor that the live connection has lost its host
  ///  (disconnected without ever re-establishing), so it runs another
  ///  discovery round instead of continuing to wait on a connection that
  ///  will never signal loss again on its own.
  virtual void NotifyConnectionLost() = 0;
};

///  @copydoc IAdapterHostSupervisor
class AdapterHostSupervisor final : public IAdapterHostSupervisor {
public:
  ///  Creates a supervisor for one adapter process's whole discovery
  ///  lifetime.
  ///  @param reader Reads a candidate endpoint from the startup rendezvous.
  ///  @param verifier Proves a candidate is the intended host before it is
  ///  trusted.
  ///  @param verifierSocket The throwaway socket `verifier` connects
  ///  through; retargeted to each candidate's port before every `Verify`
  ///  call, since only the concrete socket type exposes that reconfiguration
  ///  and `IAdapterHostHandshakeVerifier` never does so itself.
  ///  @param launcher Launches a fresh packaged host when no candidate can
  ///  be adopted or verified.
  ///  @param connectionSocket The adapter's long-lived private IPC
  ///  connection's own socket, retargeted to a verified candidate's port on
  ///  every successful round.
  ///  @param connectionProofProvider The same connection's peer-proof
  ///  provider, retargeted to a verified candidate's proof token on every
  ///  successful round.
  ///  @param connection The existing long-lived private IPC connection,
  ///  started only after the first candidate has been verified and its target
  ///  configured.
  ///  @param failedRoundBackoff The bound a round that exhausts its bounded
  ///  adopt-and-launch attempts waits before retrying.
  AdapterHostSupervisor(
      IAdapterHostRendezvousReader &reader,
      IAdapterHostHandshakeVerifier &verifier,
      ipc::WinsockAdapterIpcSocket &verifierSocket,
      IAdapterHostProcessLauncher &launcher,
      ipc::WinsockAdapterIpcSocket &connectionSocket,
      ipc::SettableAdapterIpcPeerProofProvider &connectionProofProvider,
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
  void NotifyConnectionLost() override;

private:
  ///  Runs on the background thread: repeatedly runs a discovery round and
  ///  waits for the next reason to run another, until stopped.
  void RunLoop();

  ///  Runs one bounded discovery round: adopt-and-verify a rendezvous
  ///  candidate, then launch-and-verify a fresh one if that fails.
  ///  @return The verified candidate, or `std::nullopt` if neither adoption
  ///  nor a fresh launch produced one.
  std::optional<AdapterHostEndpoint>
  RunOneDiscoveryRound(std::stop_token cancellationToken);

  ///  Retargets `verifierSocket_` to `candidate`'s port, then verifies it.
  bool VerifyCandidate(const AdapterHostEndpoint &candidate,
                       std::stop_token cancellationToken);

  ///  Retargets the live connection's socket and peer-proof provider to
  ///  `endpoint`.
  void ReconfigureLiveTarget(const AdapterHostEndpoint &endpoint);

  ///  Waits until either `NotifyConnectionLost` is called or a stop is
  ///  requested, whichever comes first.
  ///  @return `false` if a stop was requested; `true` if the connection was
  ///  reported lost.
  bool WaitForConnectionLostOrStop();

  ///  Waits out `failedRoundBackoff_`, or returns immediately once a stop is
  ///  requested.
  ///  @return `false` if a stop was requested; `true` if the backoff simply
  ///  elapsed.
  bool WaitBackoffOrStop();

  ///  Reads a candidate endpoint from the startup rendezvous.
  IAdapterHostRendezvousReader &reader_;
  ///  Proves a candidate is the intended host before it is trusted.
  IAdapterHostHandshakeVerifier &verifier_;
  ///  The throwaway socket `verifier_` connects through.
  ipc::WinsockAdapterIpcSocket &verifierSocket_;
  ///  Launches a fresh packaged host when no candidate can be adopted.
  IAdapterHostProcessLauncher &launcher_;
  ///  The adapter's long-lived private IPC connection's own socket.
  ipc::WinsockAdapterIpcSocket &connectionSocket_;
  ///  The same connection's peer-proof provider.
  ipc::SettableAdapterIpcPeerProofProvider &connectionProofProvider_;
  ///  The existing long-lived private IPC connection, started after its
  ///  first verified target is configured.
  ipc::IAdapterIpcConnection &connection_;
  ///  The bound a failed round waits before retrying.
  std::chrono::milliseconds failedRoundBackoff_;
  ///  Guards the worker thread object itself, separate from `stateMutex_` so
  ///  `Start`/`RequestStop` never contend with the condition variable's own
  ///  locking.
  std::mutex lifecycleMutex_;
  ///  The background discovery thread, started by `Start()`.
  std::thread worker_;
  ///  Guards `stopping_` and `connectionLost_`, and backs `stateCondition_`.
  std::mutex stateMutex_;
  ///  Signaled on `RequestStop()` and `NotifyConnectionLost()`.
  std::condition_variable stateCondition_;
  ///  Set by `RequestStop()`; checked before every round and wait.
  bool stopping_ = false;
  ///  Set by `NotifyConnectionLost()`; consumed by
  ///  `WaitForConnectionLostOrStop()`.
  bool connectionLost_ = false;
  ///  Cancels bounded discovery operations when shutdown begins.
  std::stop_source cancellationSource_;
};

} //  namespace dovahlink::adapter::process
