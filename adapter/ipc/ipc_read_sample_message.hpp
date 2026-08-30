#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

///  Asks the adapter to perform one host-owned opaque sample read token.
struct IpcReadSampleMessage {
  ///  The nonzero request identity used for cancellation and diagnostics.
  std::uint64_t correlationId = 0;
  ///  The nonzero token the adapter maps at the final Skyrim boundary.
  std::uint32_t sampleToken = 0;

  ///  Structural equality over every field.
  bool operator==(const IpcReadSampleMessage &) const = default;
};

} //  namespace dovahlink::adapter::ipc
