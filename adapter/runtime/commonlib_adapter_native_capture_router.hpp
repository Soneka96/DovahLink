#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <vector>

#include "capture/adapter_capture_handoff_queue.hpp"
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
///
///  Also registers and owns the `RE::LevelIncrease::Event` sink for
///  `CharacterEventKey::kCharacterLevelChanged`: a spontaneous native event
///  has no session-driven caller to hand a captured value back to the way a
///  sampled `CaptureSample` result does, so this router enqueues the event's
///  own capture directly onto `captureQueue` (given at construction) instead.
///  `LevelChangedEventSink` stays forward-declared here, defined only in the
///  `.cpp`, so this header stays free of Skyrim/SKSE runtime types, matching
///  every other `adapter/runtime` header.
class CommonLibAdapterNativeCaptureRouter final
    : public dispatch::IAdapterNativeCaptureRouter {
  public:
    ///  Creates a router that enqueues level-changed event captures onto
    ///  `captureQueue`.
    ///  @param captureQueue Must outlive this router.
    explicit CommonLibAdapterNativeCaptureRouter(capture::IAdapterCaptureHandoffQueue& captureQueue);

    ///  Declared out-of-line so `LevelChangedEventSink` need not be complete here.
    ~CommonLibAdapterNativeCaptureRouter() override;

    CommonLibAdapterNativeCaptureRouter(const CommonLibAdapterNativeCaptureRouter&) = delete;
    CommonLibAdapterNativeCaptureRouter& operator=(const CommonLibAdapterNativeCaptureRouter&) = delete;

    ///  @copydoc IAdapterNativeCaptureRouter::CaptureSample
    std::optional<std::vector<std::byte>>
    CaptureSample(std::uint32_t sampleToken) override;

    ///  @copydoc IAdapterNativeCaptureRouter::RegisterEvent
    ///  Only `CharacterEventKey::kCharacterLevelChanged` is approved; any
    ///  other key fails closed. Idempotent: `RE::BSTEventSource::AddEventSink`
    ///  itself de-duplicates by sink pointer, and this router always
    ///  registers the same owned sink instance.
    bool RegisterEvent(std::uint32_t eventKey) override;

  private:
    class LevelChangedEventSink;

    capture::IAdapterCaptureHandoffQueue& captureQueue_;
    std::unique_ptr<LevelChangedEventSink> levelChangedEventSink_;
};

} //  namespace dovahlink::adapter::runtime
