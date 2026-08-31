#pragma once

#include "process/adapter_host_constants.hpp"

#include <chrono>

namespace dovahlink::adapter::ipc {
class IAdapterIpcConnection;
} //  namespace dovahlink::adapter::ipc

namespace dovahlink::adapter::process {

class IAdapterHostSupervisor;
class IAdapterHostShutdownRequester;
class IAdapterHostProcessLauncher;

///  Runs the adapter's one fixed, ordered shutdown sequence: stop
///  discovering, ask a launched host to exit gracefully and give it a
///  bounded chance to do so before forcing it, then stop the private IPC
///  connection and release the launched host's process handle. Entirely
///  separate from the non-blocking shutdown-request signal alone --
///  `DllMain`'s `DLL_PROCESS_DETACH` may only ever call that signal
///  directly, never this orchestrator, since this sequence blocks and
///  joins threads in ways that are unsafe under the loader lock.
class IAdapterShutdownOrchestrator {
public:
  virtual ~IAdapterShutdownOrchestrator() = default;

  ///  Runs the full ordered sequence to completion:
  ///  1. Stops the host-discovery supervisor (marks it stopping, interrupts
  ///     any in-flight wait, joins its thread) without touching any
  ///     launched host's process handle or Job Object.
  ///  2. Signals the launched host's shutdown-request event.
  ///  3. Waits, bounded, for that host to exit on its own; force-terminates
  ///     it via its Job Object only if the bound elapses. A no-op wait when
  ///     no host was launched by this instance (only adopted).
  ///  4. Stops and joins the private IPC connection.
  ///  5. Releases the launched host's process handle and Job Object.
  ///  Step 5 always runs exactly once, even if an earlier step throws --
  ///  the exception is rethrown afterward rather than left to skip
  ///  releasing the handle.
  virtual void RunOrderedShutdown() = 0;
};

///  @copydoc IAdapterShutdownOrchestrator
class AdapterShutdownOrchestrator final : public IAdapterShutdownOrchestrator {
public:
  ///  Creates an orchestrator over the adapter's own already-constructed
  ///  process-lifecycle and connection collaborators.
  ///  @param supervisor Stopped first, without disturbing `launcher`'s
  ///  process handle.
  ///  @param shutdownRequester Signals the launched host's graceful
  ///  shutdown request.
  ///  @param launcher Waited on (bounded) for graceful exit, force-
  ///  terminated as a fallback, then released.
  ///  @param connection Stopped after the graceful-or-forced host shutdown
  ///  has already concluded.
  ///  @param gracefulShutdownWaitBound The bound on waiting for the
  ///  launched host to exit gracefully before forcing it.
  AdapterShutdownOrchestrator(
      IAdapterHostSupervisor &supervisor,
      IAdapterHostShutdownRequester &shutdownRequester,
      IAdapterHostProcessLauncher &launcher,
      ipc::IAdapterIpcConnection &connection,
      std::chrono::milliseconds gracefulShutdownWaitBound =
          kDefaultAdapterGracefulShutdownWaitBound);

  ///  @copydoc IAdapterShutdownOrchestrator::RunOrderedShutdown
  void RunOrderedShutdown() override;

private:
  ///  Stopped first, without disturbing the launched host's process handle.
  IAdapterHostSupervisor &supervisor_;
  ///  Signals the launched host's graceful shutdown request.
  IAdapterHostShutdownRequester &shutdownRequester_;
  ///  Waited on for graceful exit, force-terminated as a fallback, then
  ///  released.
  IAdapterHostProcessLauncher &launcher_;
  ///  Stopped after the graceful-or-forced host shutdown has concluded.
  ipc::IAdapterIpcConnection &connection_;
  ///  The bound on waiting for the launched host to exit gracefully.
  std::chrono::milliseconds gracefulShutdownWaitBound_;
};

} //  namespace dovahlink::adapter::process
