//  A tiny standalone process used only by
//  adapter_host_process_launcher_test.cpp, to exercise real Win32 process
//  launch, stdout redirection, and Job Object termination without depending
//  on the real packaged host executable. Reports a fixed PORT/PROOF/HOSTPROOF
//  endpoint over stdout, then sleeps before exiting cleanly -- long enough
//  by default to prove force-termination, or for a bounded, test-controlled
//  duration via the DOVAHLINK_TEST_HOST_SLEEP_MS environment variable, to
//  prove a graceful exit within a caller's bound. DOVAHLINK_TEST_HOST_SILENT
//  skips the report entirely, to exercise the never-reported path.
//  DOVAHLINK_TEST_HOST_SPAM instead writes endless non-newline bytes, to
//  exercise the bounded-buffer path.
//  DOVAHLINK_TEST_HOST_UNRELATED_HANDLE makes PROOF report whether the
//  supplied parent handle leaked into the child.

#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <thread>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

int main() {
  if (std::getenv("DOVAHLINK_TEST_HOST_SPAM") != nullptr) {
    while (true) {
      std::cout << "X" << std::flush;
    }
  }

  if (std::getenv("DOVAHLINK_TEST_HOST_SILENT") == nullptr) {
    //  Plain "\n", not an explicit "\r\n": Windows stdio text mode already
    //  translates every "\n" written to a non-binary stream into "\r\n", so
    //  writing "\r\n" literally here would double the "\r" on the wire.
    const char *unrelatedHandle =
        std::getenv("DOVAHLINK_TEST_HOST_UNRELATED_HANDLE");
    if (unrelatedHandle != nullptr) {
      auto rawHandle = static_cast<std::uintptr_t>(
          std::strtoull(unrelatedHandle, nullptr, 10));
      HANDLE handle = reinterpret_cast<HANDLE>(rawHandle);
      DWORD flags = 0;
      const bool inherited = GetHandleInformation(handle, &flags) != 0;
      std::cout << "PORT 4242\nPROOF " << (inherited ? "00" : "ab")
                << "\nHOSTPROOF cd\n"
                << std::flush;
    } else {
      std::cout << "PORT 4242\nPROOF ab\nHOSTPROOF cd\n" << std::flush;
    }
  }

  long sleepMilliseconds = 60000;
  if (const char *envValue = std::getenv("DOVAHLINK_TEST_HOST_SLEEP_MS")) {
    sleepMilliseconds = std::strtol(envValue, nullptr, 10);
  }
  std::this_thread::sleep_for(std::chrono::milliseconds(sleepMilliseconds));
  return 0;
}
