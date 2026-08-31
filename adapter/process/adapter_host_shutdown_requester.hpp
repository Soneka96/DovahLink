#pragma once

#include "ipc/ipc_constants.hpp"

#include <array>
#include <cstddef>
#include <string>

namespace dovahlink::adapter::process {

///  Builds the per-lifetime named shutdown-request event name a host process
///  waits on, matching the host's own `Constants.ShutdownEventName` exactly
///  (`Local\DovahLink.Host.Shutdown.<ownerLifetimeId hex>`), so a signal from
///  one Skyrim lifetime's adapter can never reach a different lifetime's
///  host.
///  @param ownerLifetimeId The owning Skyrim process's lifetime identity.
///  @return The formatted event name, ready to pass directly to a wide
///  Win32 named-object API.
std::wstring BuildShutdownEventName(
    const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>
        &ownerLifetimeId);

///  Requests that a running host process this instance's Skyrim lifetime
///  owns begin its own graceful shutdown.
class IAdapterHostShutdownRequester {
public:
  virtual ~IAdapterHostShutdownRequester() = default;

  ///  Signals the per-lifetime named shutdown event. A no-op, never
  ///  blocking and never throwing, when no host is currently listening for
  ///  it (for example because none was ever launched or it already exited).
  ///  Safe to call from `DllMain`.
  virtual void RequestShutdown() = 0;
};

///  @copydoc IAdapterHostShutdownRequester
class WindowsEventAdapterHostShutdownRequester final
    : public IAdapterHostShutdownRequester {
public:
  ///  Creates a requester for the given Skyrim lifetime's host.
  ///  @param ownerLifetimeId The owning Skyrim process's lifetime identity.
  explicit WindowsEventAdapterHostShutdownRequester(
      std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId);

  ///  @copydoc IAdapterHostShutdownRequester::RequestShutdown
  void RequestShutdown() override;

private:
  ///  The owning Skyrim process's lifetime identity.
  std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId_;
};

} //  namespace dovahlink::adapter::process
