#pragma once

#include <cstdint>
#include <string>

namespace dovahlink::adapter::ipc {

///  Sent by the host in response to `IpcTrustAdminRequestMessage`, carrying
///  the exact formatted text a Skyrim-facing console surface displays
///  verbatim. The adapter performs no interpretation or additional
///  formatting of its own; the host's trust-administration service already
///  redacts credentials and persistence exceptions before this text is built.
struct IpcTrustAdminResultMessage {
  ///  Matches the `IpcTrustAdminRequestMessage` this responds to.
  std::uint64_t correlationId = 0;
  ///  The bounded, display-ready result text.
  std::string resultText;

  ///  Structural equality over every field.
  bool operator==(const IpcTrustAdminResultMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
