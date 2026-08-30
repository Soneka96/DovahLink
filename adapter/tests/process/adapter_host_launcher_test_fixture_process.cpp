//  A tiny standalone process used only by
//  adapter_host_process_launcher_test.cpp, to exercise real Win32 process
//  launch, stdout redirection, and Job Object termination without depending
//  on the real packaged host executable. Reports a fixed PORT/PROOF
//  endpoint over stdout, then sleeps before exiting cleanly -- long enough
//  by default to prove force-termination, or for a bounded, test-controlled
//  duration via the DOVAHLINK_TEST_HOST_SLEEP_MS environment variable, to
//  prove a graceful exit within a caller's bound. DOVAHLINK_TEST_HOST_SILENT
//  skips the report entirely, to exercise the never-reported path.
//  DOVAHLINK_TEST_HOST_SPAM instead writes endless non-newline bytes, to
//  exercise the bounded-buffer path.

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <thread>

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
    std::cout << "PORT 4242\nPROOF ab\n" << std::flush;
  }

  long sleepMilliseconds = 60000;
  if (const char *envValue = std::getenv("DOVAHLINK_TEST_HOST_SLEEP_MS")) {
    sleepMilliseconds = std::strtol(envValue, nullptr, 10);
  }
  std::this_thread::sleep_for(std::chrono::milliseconds(sleepMilliseconds));
  return 0;
}
