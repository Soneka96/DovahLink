#include "process/adapter_shutdown_orchestrator.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_host_supervisor.hpp"

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
  //  Step 5 (releasing the process handle/Job Object) must run exactly once
  //  no matter how the earlier steps end, including a collaborator
  //  exception, so a failure partway through this sequence never leaks the
  //  launched host's handle. The exception itself still propagates to the
  //  caller once release has happened.
  try {
    //  Steps 1-2: stop discovering. Never touches the launcher's process
    //  handle or Job Object.
    supervisor_.RequestStop();

    //  Step 3: signal graceful shutdown, then wait bounded for it, force-
    //  terminating via the Job Object only if the bound elapses. A no-op
    //  wait when this instance only adopted an existing host.
    shutdownRequester_.RequestShutdown();
    launcher_.AwaitExitOrTerminate(gracefulShutdownWaitBound_);

    //  Step 4: stop the connection only after the host shutdown attempt has
    //  already concluded, one way or the other.
    connection_.Stop();
  } catch (...) {
    launcher_.Release();
    throw;
  }

  //  Step 5: release the process handle/Job Object last, never before the
  //  graceful attempt in step 3 had its chance.
  launcher_.Release();
}

} //  namespace dovahlink::adapter::process
