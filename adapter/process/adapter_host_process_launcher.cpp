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
#include <vector>

namespace dovahlink::adapter::process {

namespace {

///  Closes one Win32 handle on scope exit, unless ownership is released.
class ScopedHandle {
public:
  ///  Takes ownership of `handle`, which may be null.
  explicit ScopedHandle(HANDLE handle) : handle_(handle) {}

  ///  Closes the owned handle, if any.
  ~ScopedHandle() {
    if (handle_ != nullptr) {
      CloseHandle(handle_);
    }
  }

  ScopedHandle(const ScopedHandle &) = delete;
  ScopedHandle &operator=(const ScopedHandle &) = delete;

  ///  Returns the currently owned handle without transferring ownership.
  HANDLE Get() const { return handle_; }

  ///  Closes the currently owned handle and takes ownership of `handle`.
  void Reset(HANDLE handle = nullptr) {
    if (handle_ != nullptr) {
      CloseHandle(handle_);
    }
    handle_ = handle;
  }

  ///  Releases the owned handle to the caller.
  HANDLE Release() {
    HANDLE handle = handle_;
    handle_ = nullptr;
    return handle;
  }

private:
  ///  The handle owned by this scope, or null.
  HANDLE handle_ = nullptr;
};

///  Deletes one initialized process-thread attribute list on scope exit.
class ScopedProcThreadAttributeList {
public:
  ///  Takes ownership of an initialized attribute list, which may be null.
  explicit ScopedProcThreadAttributeList(
      LPPROC_THREAD_ATTRIBUTE_LIST attributeList)
      : attributeList_(attributeList) {}

  ///  Deletes the owned attribute list, if any.
  ~ScopedProcThreadAttributeList() {
    if (attributeList_ != nullptr) {
      DeleteProcThreadAttributeList(attributeList_);
    }
  }

  ScopedProcThreadAttributeList(const ScopedProcThreadAttributeList &) = delete;
  ScopedProcThreadAttributeList &
  operator=(const ScopedProcThreadAttributeList &) = delete;

private:
  ///  The initialized attribute list owned by this scope, or null.
  LPPROC_THREAD_ATTRIBUTE_LIST attributeList_ = nullptr;
};

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
                         std::chrono::steady_clock::time_point deadline,
                         std::stop_token cancellationToken) {
  std::string buffer;
  while (true) {
    if (cancellationToken.stop_requested() ||
        std::chrono::steady_clock::now() >= deadline) {
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

std::optional<AdapterHostEndpoint>
Win32AdapterHostProcessLauncher::Launch(std::stop_token cancellationToken) {
  ReleaseCurrentProcess();
  if (cancellationToken.stop_requested()) {
    return std::nullopt;
  }

  SECURITY_ATTRIBUTES pipeAttributes{};
  pipeAttributes.nLength = sizeof(pipeAttributes);
  pipeAttributes.bInheritHandle = TRUE;
  HANDLE pipeRead = nullptr;
  HANDLE pipeWrite = nullptr;
  if (!CreatePipe(&pipeRead, &pipeWrite, &pipeAttributes, 0)) {
    return std::nullopt;
  }
  ScopedHandle pipeReadGuard(pipeRead);
  ScopedHandle pipeWriteGuard(pipeWrite);
  if (!SetHandleInformation(pipeRead, HANDLE_FLAG_INHERIT, 0)) {
    return std::nullopt;
  }

  HANDLE jobHandle = CreateJobObjectW(nullptr, nullptr);
  if (jobHandle == nullptr) {
    return std::nullopt;
  }
  ScopedHandle jobHandleGuard(jobHandle);

  JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
  limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
  if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation,
                               &limits, sizeof(limits))) {
    return std::nullopt;
  }

  SIZE_T attributeListSize = 0;
  InitializeProcThreadAttributeList(nullptr, 2, 0, &attributeListSize);
  if (attributeListSize == 0) {
    return std::nullopt;
  }

  std::vector<std::byte> attributeStorage(attributeListSize);
  auto *attributeList =
      reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributeStorage.data());
  if (!InitializeProcThreadAttributeList(attributeList, 2, 0,
                                         &attributeListSize)) {
    return std::nullopt;
  }
  ScopedProcThreadAttributeList attributeListGuard(attributeList);

  HANDLE jobHandles[] = {jobHandle};
  if (!UpdateProcThreadAttribute(attributeList, 0,
                                 PROC_THREAD_ATTRIBUTE_JOB_LIST, jobHandles,
                                 sizeof(jobHandles), nullptr, nullptr)) {
    return std::nullopt;
  }

  HANDLE inheritedHandles[] = {pipeWrite};
  if (!UpdateProcThreadAttribute(
          attributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inheritedHandles,
          sizeof(inheritedHandles), nullptr, nullptr)) {
    return std::nullopt;
  }

  STARTUPINFOW startupInfo{};
  startupInfo.cb = sizeof(startupInfo);
  startupInfo.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
  startupInfo.wShowWindow = SW_HIDE;
  startupInfo.hStdOutput = pipeWrite;
  startupInfo.hStdError = pipeWrite;
  startupInfo.hStdInput = nullptr;

  STARTUPINFOEXW startupInfoEx{};
  startupInfoEx.StartupInfo = startupInfo;
  startupInfoEx.StartupInfo.cb = sizeof(startupInfoEx);
  startupInfoEx.lpAttributeList = attributeList;
  startupInfoEx.StartupInfo.hStdOutput = pipeWriteGuard.Get();
  startupInfoEx.StartupInfo.hStdError = pipeWriteGuard.Get();

  std::wstring commandLine =
      BuildCommandLine(executablePath_, ownerLifetimeId_);

  PROCESS_INFORMATION processInfo{};
  //  CREATE_NO_WINDOW, not just STARTF_USESHOWWINDOW/SW_HIDE alone: the
  //  latter only hides a GUI window, but a console-subsystem host process
  //  would otherwise still flash an allocated console window.
  BOOL created = CreateProcessW(
      nullptr, commandLine.data(), nullptr, nullptr,
      /*bInheritHandles=*/TRUE, CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
      nullptr, nullptr, &startupInfoEx.StartupInfo, &processInfo);

  pipeWriteGuard.Reset();
  if (!created) {
    return std::nullopt;
  }

  ScopedHandle processHandle(processInfo.hProcess);
  ScopedHandle threadHandle(processInfo.hThread);

  if (cancellationToken.stop_requested()) {
    TerminateJobObject(jobHandleGuard.Get(), 1);
    WaitForSingleObject(
        processHandle.Get(),
        static_cast<DWORD>(kAdapterHostForceTerminateGraceWait.count()));
    return std::nullopt;
  }

  std::chrono::steady_clock::time_point deadline =
      std::chrono::steady_clock::now() + launchTimeout_;
  std::optional<std::pair<std::string, std::string>> lines =
      ReadTwoLinesWithDeadline(pipeReadGuard.Get(), deadline,
                               cancellationToken);

  std::optional<AdapterHostEndpoint> endpoint;
  if (lines.has_value()) {
    endpoint = TryParseHostEndpointReport(lines->first, lines->second);
  }
  if (!endpoint.has_value() || cancellationToken.stop_requested()) {
    TerminateJobObject(jobHandleGuard.Get(), 1);
    return std::nullopt;
  }

  processHandle_ = processHandle.Release();
  jobHandle_ = jobHandleGuard.Release();
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
