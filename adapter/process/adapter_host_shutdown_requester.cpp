#include "process/adapter_host_shutdown_requester.hpp"

#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

namespace dovahlink::adapter::process {

std::wstring BuildShutdownEventName(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>
        &ownerLifetimeId) {
  std::string hex = FormatOwnerLifetimeId(ownerLifetimeId);
  std::wstring name = L"Local\\DovahLink.Host.Shutdown.";
  name.append(hex.begin(), hex.end());
  return name;
}

WindowsEventAdapterHostShutdownRequester::
    WindowsEventAdapterHostShutdownRequester(
        std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId)
    : ownerLifetimeId_(ownerLifetimeId) {}

void WindowsEventAdapterHostShutdownRequester::RequestShutdown() {
  std::wstring eventName = BuildShutdownEventName(ownerLifetimeId_);
  HANDLE eventHandle = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName.c_str());
  if (eventHandle == nullptr) {
    //  No host is currently listening for this lifetime's shutdown event.
    return;
  }
  SetEvent(eventHandle);
  CloseHandle(eventHandle);
}

} //  namespace dovahlink::adapter::process
