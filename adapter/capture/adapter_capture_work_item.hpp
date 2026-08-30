#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace dovahlink::adapter::capture {

///  An owned, bounded capture result produced at a Skyrim callback boundary and
///  handed off to worker-owned code. Holds no Skyrim/CommonLib pointer or
///  borrowed buffer; every field is a plain owned value that remains valid for
///  as long as the item itself does.
struct AdapterCaptureWorkItem {
  ///  The host-directed event key or sample token this value was captured for.
  std::uint32_t intentKey = 0;
  ///  The captured value, already copied out of Skyrim state at the callback
  ///  boundary.
  std::vector<std::byte> capturedValue;

  ///  Structural equality over every field.
  bool operator==(const AdapterCaptureWorkItem &) const = default;
};

} //  namespace dovahlink::adapter::capture
