#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_owner_lifetime_id.hpp"

#include <windows.h>

#include <chrono>
#include <iostream>

using dovahlink::adapter::process::DeriveOwnerLifetimeId;
using dovahlink::adapter::process::FormatOwnerLifetimeId;
using dovahlink::adapter::process::Win32AdapterHostProcessLauncher;

int main() {
  const auto ownerLifetimeId = DeriveOwnerLifetimeId();
  if (!ownerLifetimeId.has_value()) {
    return 2;
  }
  Win32AdapterHostProcessLauncher launcher(
      DOVAHLINK_HOST_EXECUTABLE, *ownerLifetimeId, std::chrono::seconds(10));
  auto endpoint = launcher.Launch();
  if (!endpoint.has_value()) {
    return 1;
  }

  std::cout << "OWNER " << FormatOwnerLifetimeId(*ownerLifetimeId) << '\n'
            << "PORT " << endpoint->port << '\n'
            << "HOST_PID " << launcher.ProcessId() << '\n'
            << std::flush;

  //  The parent test terminates this owner process abruptly. If the launcher's
  //  Job Object supervision is correct, Windows closes the Job Object handle
  //  and terminates the real host as part of that owner termination.
  WaitForSingleObject(GetCurrentProcess(), INFINITE);
  return 0;
}
