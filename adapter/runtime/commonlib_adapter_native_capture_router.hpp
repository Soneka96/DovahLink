#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <vector>

#include "dispatch/adapter_native_capture_router.hpp"

namespace dovahlink::adapter::runtime {

///  The real, CommonLib-backed `IAdapterNativeCaptureRouter` implementation
///  for Stage 4's narrow first capture slice, per
///  `roadmap/04-live-state-synchronization-foundation.md`'s "Real capture
///  and host integration". Maps each host-owned `CharacterSampleToken` to its
///  one approved `CommonLibCharacterCapture` read and encodes the result
///  into the little-endian wire payload the host's own `LiveCaptureSink`
///  decodes: 12 bytes (three float32: health, magicka, stamina) for vitals,
///  4 bytes (one float32) for XP, 2 bytes (one uint16) for the level
///  baseline. An unknown token, or a known token whose underlying read is
///  currently unavailable, both return `std::nullopt` --
///  `IAdapterNativeCaptureRouter::CaptureSample`'s own contract does not
///  distinguish the two; the host applies the same "unavailable" treatment
///  to either.
class CommonLibAdapterNativeCaptureRouter final
    : public dispatch::IAdapterNativeCaptureRouter {
  public:
    ///  @copydoc IAdapterNativeCaptureRouter::CaptureSample
    std::optional<std::vector<std::byte>>
    CaptureSample(std::uint32_t sampleToken) override;

    ///  @copydoc IAdapterNativeCaptureRouter::RegisterEvent
    ///  Not yet implemented: always returns `false`. The level-changed
    ///  native-event sink lands in a following step.
    bool RegisterEvent(std::uint32_t eventKey) override;
};

} //  namespace dovahlink::adapter::runtime
