#pragma once

#include "ipc/ipc_constants.hpp"
#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_endpoint.hpp"

#include <array>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <optional>

namespace dovahlink::adapter::process {

///  Launches the packaged host process as a hidden, Job-Object-supervised
///  child, and waits for it to report the endpoint it bound. Discovery
///  only, never authentication: the reported endpoint must still pass the
///  full mutual Hello/HelloAck handshake before it is trusted.
class IAdapterHostProcessLauncher {
public:
  virtual ~IAdapterHostProcessLauncher() = default;

  ///  Launches the packaged host process hidden, passing this launcher's
  ///  configured owner-lifetime-id as its sole structured process argument,
  ///  and waits (bounded) for it to report its bound endpoint over its
  ///  redirected stdout. Assigns the child to a Job Object configured to
  ///  terminate it the moment that Job Object handle closes, so an adapter
  ///  crash or forced termination can never leave it orphaned. Releases any
  ///  process this launcher previously launched before starting a new one.
  ///  @return The endpoint the host reported, or `std::nullopt` if the
  ///  process could not be started, could not be placed under Job Object
  ///  supervision, or never reported a well-formed endpoint within the
  ///  configured bound.
  virtual std::optional<AdapterHostEndpoint> Launch() = 0;

  ///  Waits, bounded by `timeout`, for the process this launcher most
  ///  recently launched to exit on its own; force-terminates it via its Job
  ///  Object only if `timeout` elapses first. A no-op that returns `true`
  ///  when this launcher has not launched a process it still holds a handle
  ///  to (for example because the current endpoint was adopted from an
  ///  existing host instead of launched).
  ///  @return `true` if the process had already exited, or there was none
  ///  to wait on; `false` if force-termination was required.
  virtual bool AwaitExitOrTerminate(std::chrono::milliseconds timeout) = 0;
};

///  @copydoc IAdapterHostProcessLauncher
class Win32AdapterHostProcessLauncher final
    : public IAdapterHostProcessLauncher {
public:
  ///  Creates a launcher for one adapter process's repeated launch attempts.
  ///  @param executablePath The packaged host executable to launch. Path
  ///  resolution (finding the adapter plugin's own installed directory) is
  ///  the composition root's responsibility, not this class's.
  ///  @param ownerLifetimeId The owning Skyrim process's lifetime identity,
  ///  passed to every launched process as its structured argument.
  ///  @param launchTimeout The bound on waiting for a launched process to
  ///  report its endpoint.
  Win32AdapterHostProcessLauncher(
      std::filesystem::path executablePath,
      std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
      std::chrono::milliseconds launchTimeout =
          kDefaultAdapterHostLaunchTimeout);

  ///  Releases any process and Job Object handle this instance still holds.
  ~Win32AdapterHostProcessLauncher() override;

  Win32AdapterHostProcessLauncher(const Win32AdapterHostProcessLauncher &) =
      delete;
  Win32AdapterHostProcessLauncher &
  operator=(const Win32AdapterHostProcessLauncher &) = delete;

  ///  @copydoc IAdapterHostProcessLauncher::Launch
  std::optional<AdapterHostEndpoint> Launch() override;

  ///  @copydoc IAdapterHostProcessLauncher::AwaitExitOrTerminate
  bool AwaitExitOrTerminate(std::chrono::milliseconds timeout) override;

private:
  ///  Closes any process and Job Object handle this instance currently
  ///  holds, resetting both to none held. Safe even if the process is still
  ///  running: closing its Job Object handle triggers that Job Object's
  ///  own kill-on-close termination, so this never leaves an orphan behind.
  void ReleaseCurrentProcess();

  ///  The packaged host executable this launcher starts.
  std::filesystem::path executablePath_;
  ///  The owning Skyrim process's lifetime identity, passed to every
  ///  launched process.
  std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId_;
  ///  The bound on waiting for a launched process to report its endpoint.
  std::chrono::milliseconds launchTimeout_;
  ///  The underlying Win32 `HANDLE` of the most recently launched process
  ///  this instance still holds, or `nullptr` if none. Opaque here to keep
  ///  `<windows.h>` out of this header.
  void *processHandle_ = nullptr;
  ///  The underlying Win32 `HANDLE` of that process's Job Object, or
  ///  `nullptr` if none. Opaque for the same reason as `processHandle_`.
  void *jobHandle_ = nullptr;
};

} //  namespace dovahlink::adapter::process
