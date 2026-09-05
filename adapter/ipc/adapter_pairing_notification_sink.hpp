#pragma once

#include "ipc/ipc_enums.hpp"

#include <string>

namespace dovahlink::adapter::ipc {

///  The adapter's Skyrim-facing pairing-code display seam. The host has
///  already decided the code and display intent; this sink only presents it
///  and never makes a pairing, trust, authorization, or retry decision of its
///  own. Every method is called only from the Skyrim game thread.
class IAdapterPairingNotificationSink {
public:
  ///  Releases the interface without performing work.
  virtual ~IAdapterPairingNotificationSink() = default;

  ///  Presents `code` at the Skyrim-facing display seam for the given
  ///  intent.
  ///  @param code The code to display.
  ///  @param mode Which display intent this request carries.
  ///  @return Whether the display seam accepted and presented the code.
  virtual bool Display(const std::string &code, PairingDisplayMode mode) = 0;

  ///  Presents a no-code terminal notification once the wrong-attempt hard
  ///  limit has cancelled the active pairing challenge. Best effort; no
  ///  result to report.
  virtual void NotifyAttemptsExhausted() = 0;
};

} //  namespace dovahlink::adapter::ipc
