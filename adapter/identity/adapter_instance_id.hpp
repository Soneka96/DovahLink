#pragma once

#include <array>
#include <cstddef>

namespace dovahlink::adapter::identity {

///  Identifies one native adapter connection instance -- the private IPC
///  `IpcHelloMessage::adapterInstanceId` payload. An adapter restart (SKSE
///  plugin reload or a Skyrim process restart) always produces a new value,
///  matching `ai/context/host/architecture.md`'s "Restart behavior" for
///  `adapterInstanceId`.
struct AdapterInstanceId {
  ///  The underlying 16 opaque identity bytes.
  std::array<std::byte, 16> value{};

  ///  Structural equality over the underlying value.
  bool operator==(const AdapterInstanceId &) const = default;
};

} //  namespace dovahlink::adapter::identity
