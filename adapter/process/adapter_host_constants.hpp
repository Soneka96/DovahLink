#pragma once

#include <chrono>
#include <cstddef>
#include <filesystem>

namespace dovahlink::adapter::process {

//  ---- Handshake verification ----

///  The default bound `AdapterHostHandshakeVerifier` waits for a candidate's
///  `IpcHelloAckMessage` before treating it as an unreachable or
///  non-responding peer. Approved as a provisional value for this concept's
///  loopback-only, same-machine handshake; a later concept may revise it with
///  the same documented approval `ai/context/protocol/security.md`'s own
///  limits require.
inline constexpr std::chrono::milliseconds
    kDefaultAdapterHostHandshakeVerifyTimeout{2000};

//  ---- Process launch ----

///  The packaged host executable's path relative to the adapter plugin's own
///  directory. Packaging the final release layout is a non-goal of this
///  concept; this records the assumed layout a future packaging step must
///  honor -- the host executable installed as a sibling
///  `DovahLink.Host/DovahLink.Host.exe` directory beside the adapter plugin
///  DLL. Combined with that directory by the plugin composition root, which
///  is the only place able to resolve its own module path.
inline const std::filesystem::path kAdapterHostExecutableRelativePath =
    "DovahLink.Host/DovahLink.Host.exe";

///  The default bound `Win32AdapterHostProcessLauncher` waits for a newly
///  launched host process to report its endpoint over its redirected
///  stdout, before treating the launch as failed.
inline constexpr std::chrono::milliseconds kDefaultAdapterHostLaunchTimeout{
    5000};

///  How often `Win32AdapterHostProcessLauncher` polls a launched process's
///  redirected stdout pipe for new bytes. Anonymous pipes do not support
///  overlapped (asynchronous) I/O, so a short poll is this concept's bounded
///  alternative to a blocking read with no timeout.
inline constexpr std::chrono::milliseconds kAdapterHostLaunchStdoutPollInterval{
    20};

///  The maximum bytes `Win32AdapterHostProcessLauncher` buffers from a
///  launched process's stdout while waiting for its two-line endpoint
///  report, so a launched process that never produces a newline cannot grow
///  that buffer unbounded.
inline constexpr std::size_t kMaxAdapterHostEndpointReportBytes = 1024;

///  The bound `Win32AdapterHostProcessLauncher::AwaitExitOrTerminate` waits
///  for the operating system to finish tearing down a process after
///  force-termination is requested, before returning. Force-termination
///  itself is effectively immediate; this only bounds the brief window
///  between requesting it and the OS actually reclaiming the process.
inline constexpr std::chrono::milliseconds kAdapterHostForceTerminateGraceWait{
    5000};

} //  namespace dovahlink::adapter::process
