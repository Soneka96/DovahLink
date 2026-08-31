#include "process/adapter_shutdown_orchestrator.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_host_supervisor.hpp"

#include <exception>

namespace dovahlink::adapter::process {

AdapterShutdownOrchestrator::AdapterShutdownOrchestrator(
    IAdapterHostSupervisor &supervisor,
    IAdapterHostShutdownRequester &shutdownRequester,
    IAdapterHostProcessLauncher &launcher,
    ipc::IAdapterIpcConnection &connection,
    std::chrono::milliseconds gracefulShutdownWaitBound)
    : supervisor_(supervisor), shutdownRequester_(shutdownRequester),
      launcher_(launcher), connection_(connection),
      gracefulShutdownWaitBound_(gracefulShutdownWaitBound) {}

void AdapterShutdownOrchestrator::RunOrderedShutdown() {
  std::exception_ptr firstException;
  auto runPhase = [&firstException](auto &&phase) noexcept {
    try {
      phase();
    } catch (...) {
      //  Continue the ordered cleanup so a failure in one phase cannot skip
      //  the graceful shutdown attempt or the final process-handle release.
      if (!firstException) {
        firstException = std::current_exception();
      }
    }
  };

  //  Steps 1-2: stop discovering. Never touches the launcher's process handle
  //  or Job Object.
  runPhase([this] { supervisor_.RequestStop(); });

  //  Step 3: signal graceful shutdown, then wait bounded for it, force-
  //  terminating via the Job Object only if the bound elapses. A no-op wait
  //  when this instance only adopted an existing host.
  runPhase([this] { shutdownRequester_.RequestShutdown(); });
  runPhase(
      [this] { launcher_.AwaitExitOrTerminate(gracefulShutdownWaitBound_); });

  //  Step 4: stop the connection only after the host shutdown attempt has
  //  already concluded, one way or the other.
  runPhase([this] { connection_.Stop(); });

  //  Step 5: release the process handle/Job Object last, never before the
  //  graceful attempt in step 3 had its chance.
  runPhase([this] { launcher_.Release(); });

  if (firstException) {
    std::rethrow_exception(firstException);
  }
}

} //  namespace dovahlink::adapter::process
