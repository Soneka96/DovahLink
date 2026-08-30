#include "process/adapter_host_process_launcher.hpp"

#include "process/adapter_host_endpoint_report.hpp"
#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <algorithm>
#include <array>
#include <string>
#include <thread>
#include <utility>

namespace dovahlink::adapter::process {

namespace {

///  Builds the structured command line `"<executablePath>" <ownerLifetimeId
///  hex>` for `CreateProcessW`. This is never passed through a shell:
///  `CreateProcessW` parses this string with its own argv convention, not
///  command-interpreter syntax.
std::wstring
BuildCommandLine(const std::filesystem::path &executablePath,
                 const std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes>
                     &ownerLifetimeId) {
  std::wstring commandLine = L"\"";
  commandLine += executablePath.native();
  commandLine += L"\" ";
  std::string hex = FormatOwnerLifetimeId(ownerLifetimeId);
  commandLine.append(hex.begin(), hex.end());
  return commandLine;
}

///  Reads from `pipeRead` until two `\n`-terminated lines have accumulated,
///  or `deadline` elapses, polling in short intervals since anonymous pipes
///  support no asynchronous read with a timeout.
///  @return The two raw lines (each still possibly carrying a trailing
///  `\r`), or `std::nullopt` if the pipe ended or `deadline` elapsed first.
std::optional<std::pair<std::string, std::string>>
ReadTwoLinesWithDeadline(HANDLE pipeRead,
                         std::chrono::steady_clock::time_point deadline) {
  std::string buffer;
  while (true) {
    if (std::chrono::steady_clock::now() >= deadline) {
      return std::nullopt;
    }

    DWORD available = 0;
    if (!PeekNamedPipe(pipeRead, nullptr, 0, nullptr, &available, nullptr)) {
      //  The write end closed (the process exited) without ever producing a
      //  complete report.
      return std::nullopt;
    }
    if (available == 0) {
      std::this_thread::sleep_for(kAdapterHostLaunchStdoutPollInterval);
      continue;
    }

    std::array<char, 256> chunk{};
    DWORD toRead =
        static_cast<DWORD>(std::min<std::size_t>(available, chunk.size()));
    DWORD bytesRead = 0;
    if (!ReadFile(pipeRead, chunk.data(), toRead, &bytesRead, nullptr)) {
      return std::nullopt;
    }
    buffer.append(chunk.data(), bytesRead);
    if (buffer.size() > kMaxAdapterHostEndpointReportBytes) {
      return std::nullopt;
    }

    std::size_t firstNewline = buffer.find('\n');
    if (firstNewline == std::string::npos) {
      continue;
    }
    std::size_t secondNewline = buffer.find('\n', firstNewline + 1);
    if (secondNewline == std::string::npos) {
      continue;
    }

    return std::make_pair(
        buffer.substr(0, firstNewline),
        buffer.substr(firstNewline + 1, secondNewline - firstNewline - 1));
  }
}

} //  namespace

Win32AdapterHostProcessLauncher::Win32AdapterHostProcessLauncher(
    std::filesystem::path executablePath,
    std::array<std::byte, ipc::kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
    std::chrono::milliseconds launchTimeout)
    : executablePath_(std::move(executablePath)),
      ownerLifetimeId_(ownerLifetimeId), launchTimeout_(launchTimeout) {}

Win32AdapterHostProcessLauncher::~Win32AdapterHostProcessLauncher() {
  ReleaseCurrentProcess();
}

std::optional<AdapterHostEndpoint> Win32AdapterHostProcessLauncher::Launch() {
  ReleaseCurrentProcess();

  SECURITY_ATTRIBUTES pipeAttributes{};
  pipeAttributes.nLength = sizeof(pipeAttributes);
  pipeAttributes.bInheritHandle = TRUE;
  HANDLE pipeRead = nullptr;
  HANDLE pipeWrite = nullptr;
  if (!CreatePipe(&pipeRead, &pipeWrite, &pipeAttributes, 0)) {
    return std::nullopt;
  }
  if (!SetHandleInformation(pipeRead, HANDLE_FLAG_INHERIT, 0)) {
    CloseHandle(pipeRead);
    CloseHandle(pipeWrite);
    return std::nullopt;
  }

  STARTUPINFOW startupInfo{};
  startupInfo.cb = sizeof(startupInfo);
  startupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
  startupInfo.wShowWindow = SW_HIDE;
  startupInfo.hStdOutput = pipeWrite;
  startupInfo.hStdError = pipeWrite;
  startupInfo.hStdInput = nullptr;

  std::wstring commandLine =
      BuildCommandLine(executablePath_, ownerLifetimeId_);

  PROCESS_INFORMATION processInfo{};
  //  CREATE_NO_WINDOW, not just STARTF_USESHOWWINDOW/SW_HIDE alone: the
  //  latter only hides a GUI window, but a console-subsystem host process
  //  would otherwise still flash an allocated console window.
  BOOL created = CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr,
                                /*bInheritHandles=*/TRUE,
                                CREATE_NO_WINDOW | CREATE_SUSPENDED, nullptr,
                                nullptr, &startupInfo, &processInfo);

  CloseHandle(pipeWrite);
  if (!created) {
    CloseHandle(pipeRead);
    return std::nullopt;
  }

  HANDLE jobHandle = CreateJobObjectW(nullptr, nullptr);
  bool jobConfigured = false;
  if (jobHandle != nullptr) {
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags =
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    jobConfigured =
        SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation,
                                &limits, sizeof(limits)) &&
        AssignProcessToJobObject(jobHandle, processInfo.hProcess);
  }
  if (!jobConfigured) {
    //  Without a configured Job Object, a crashed or forcibly terminated
    //  adapter could leave this host orphaned -- exactly what the "no
    //  orphaned host" invariant forbids. Fail the launch rather than
    //  continue without that safety net.
    if (jobHandle != nullptr) {
      //  This is normally an unassigned job because either configuration or
      //  assignment failed, but a best-effort job termination closes the
      //  remaining safety path before the handle is released.
      TerminateJobObject(jobHandle, 1);
    }
    BOOL terminated = TerminateProcess(processInfo.hProcess, 1);
    DWORD terminationWait = WaitForSingleObject(
        processInfo.hProcess,
        static_cast<DWORD>(kAdapterHostForceTerminateGraceWait.count()));
    if ((!terminated || terminationWait != WAIT_OBJECT_0) &&
        jobHandle != nullptr) {
      TerminateJobObject(jobHandle, 1);
      terminationWait = WaitForSingleObject(
          processInfo.hProcess,
          static_cast<DWORD>(kAdapterHostForceTerminateGraceWait.count()));
    }
    if (jobHandle != nullptr) {
      CloseHandle(jobHandle);
    }
    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    CloseHandle(pipeRead);
    return std::nullopt;
  }

  //  Resume only after the child is inside the kill-on-close Job Object.
  //  This closes the crash window in which Skyrim could disappear after
  //  CreateProcessW but before parent-lifetime supervision was established.
  if (ResumeThread(processInfo.hThread) == static_cast<DWORD>(-1)) {
    TerminateJobObject(jobHandle, 1);
    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    CloseHandle(jobHandle);
    CloseHandle(pipeRead);
    return std::nullopt;
  }
  CloseHandle(processInfo.hThread);

  std::chrono::steady_clock::time_point deadline =
      std::chrono::steady_clock::now() + launchTimeout_;
  std::optional<std::pair<std::string, std::string>> lines =
      ReadTwoLinesWithDeadline(pipeRead, deadline);
  CloseHandle(pipeRead);

  std::optional<AdapterHostEndpoint> endpoint;
  if (lines.has_value()) {
    endpoint = TryParseHostEndpointReport(lines->first, lines->second);
  }
  if (!endpoint.has_value()) {
    TerminateJobObject(jobHandle, 1);
    CloseHandle(processInfo.hProcess);
    CloseHandle(jobHandle);
    return std::nullopt;
  }

  processHandle_ = processInfo.hProcess;
  jobHandle_ = jobHandle;
  return endpoint;
}

std::uint32_t Win32AdapterHostProcessLauncher::ProcessId() const {
  if (processHandle_ == nullptr) {
    return 0;
  }
  return static_cast<std::uint32_t>(
      GetProcessId(static_cast<HANDLE>(processHandle_)));
}

bool Win32AdapterHostProcessLauncher::AwaitExitOrTerminate(
    std::chrono::milliseconds timeout) {
  if (processHandle_ == nullptr) {
    return true;
  }

  HANDLE process = static_cast<HANDLE>(processHandle_);
  DWORD waitResult =
      WaitForSingleObject(process, static_cast<DWORD>(timeout.count()));
  if (waitResult == WAIT_OBJECT_0) {
    return true;
  }

  TerminateJobObject(static_cast<HANDLE>(jobHandle_), 1);
  WaitForSingleObject(
      process, static_cast<DWORD>(kAdapterHostForceTerminateGraceWait.count()));
  return false;
}

void Win32AdapterHostProcessLauncher::Release() { ReleaseCurrentProcess(); }

void Win32AdapterHostProcessLauncher::ReleaseCurrentProcess() {
  if (processHandle_ != nullptr) {
    CloseHandle(static_cast<HANDLE>(processHandle_));
    processHandle_ = nullptr;
  }
  if (jobHandle_ != nullptr) {
    CloseHandle(static_cast<HANDLE>(jobHandle_));
    jobHandle_ = nullptr;
  }
}

} //  namespace dovahlink::adapter::process
