#include "runtime/commonlib_adapter_native_capture_router.hpp"

#include "RE/Skyrim.h"

#include <algorithm>
#include <array>
#include <iterator>

#include "capture/live_state_sample_codec.hpp"
#include "enums.hpp"
#include "runtime/commonlib_adapter_character_capture.hpp"

namespace dovahlink::adapter::runtime {

///  The real `RE::LevelIncrease::Event` sink. Defined only here so the
///  owning header stays Skyrim/SKSE-free; see the header's own doc comment.
class CommonLibAdapterNativeCaptureRouter::LevelChangedEventSink final
    : public RE::BSTEventSink<RE::LevelIncrease::Event> {
  public:
    explicit LevelChangedEventSink(capture::IAdapterCaptureHandoffQueue& captureQueue)
        : captureQueue_(captureQueue) {}

    ///  Copies the new level and enqueues it. The queue may reject an item
    ///  at capacity; reliable-Event loss under sustained capture-queue
    ///  pressure is a known, documented open risk (see
    ///  `roadmap/04-live-state-synchronization-foundation.md`'s "Real
    ///  capture and host integration"), not silently handled here.
    RE::BSEventNotifyControl
    ProcessEvent(const RE::LevelIncrease::Event* event,
                 RE::BSTEventSource<RE::LevelIncrease::Event>*) override {
        if (event == nullptr) {
            return RE::BSEventNotifyControl::kContinue;
        }
        std::array<std::byte, 2> encoded =
            capture::EncodeUInt16LittleEndian(event->newLevel);
        captureQueue_.TryEnqueue(capture::AdapterCaptureWorkItem{
            .intentKey = static_cast<std::uint32_t>(
                capture::CharacterEventKey::kCharacterLevelChanged),
            .capturedValue = std::vector<std::byte>(encoded.begin(), encoded.end()),
            .correlationId = 0,
            .source = capture::CaptureSourceKind::kEvent,
            .availability = capture::CaptureAvailability::kAvailable,
            .playContextId = {},
        });
        return RE::BSEventNotifyControl::kContinue;
    }

  private:
    capture::IAdapterCaptureHandoffQueue& captureQueue_;
};

CommonLibAdapterNativeCaptureRouter::CommonLibAdapterNativeCaptureRouter(
    capture::IAdapterCaptureHandoffQueue& captureQueue)
    : captureQueue_(captureQueue),
      levelChangedEventSink_(std::make_unique<LevelChangedEventSink>(captureQueue)) {}

CommonLibAdapterNativeCaptureRouter::~CommonLibAdapterNativeCaptureRouter() = default;

std::optional<std::vector<std::byte>>
CommonLibAdapterNativeCaptureRouter::CaptureSample(std::uint32_t sampleToken) {
    switch (static_cast<capture::CharacterSampleToken>(sampleToken)) {
    case capture::CharacterSampleToken::kCharacterVitals: {
        std::optional<CharacterVitalsCapture> vitals = CaptureCharacterVitals();
        if (!vitals) {
            return std::nullopt;
        }
        std::vector<std::byte> payload;
        payload.reserve(12);
        std::array<std::byte, 4> health = capture::EncodeFloatLittleEndian(vitals->health);
        std::array<std::byte, 4> magicka = capture::EncodeFloatLittleEndian(vitals->magicka);
        std::array<std::byte, 4> stamina = capture::EncodeFloatLittleEndian(vitals->stamina);
        std::ranges::copy(health, std::back_inserter(payload));
        std::ranges::copy(magicka, std::back_inserter(payload));
        std::ranges::copy(stamina, std::back_inserter(payload));
        return payload;
    }
    case capture::CharacterSampleToken::kCharacterXp: {
        std::optional<float> xp = CaptureCharacterXp();
        if (!xp) {
            return std::nullopt;
        }
        std::array<std::byte, 4> encoded = capture::EncodeFloatLittleEndian(*xp);
        return std::vector<std::byte>(encoded.begin(), encoded.end());
    }
    case capture::CharacterSampleToken::kCharacterLevelBaseline: {
        std::optional<std::uint16_t> level = CaptureCharacterLevel();
        if (!level) {
            return std::nullopt;
        }
        std::array<std::byte, 2> encoded = capture::EncodeUInt16LittleEndian(*level);
        return std::vector<std::byte>(encoded.begin(), encoded.end());
    }
    default:
        return std::nullopt;
    }
}

bool CommonLibAdapterNativeCaptureRouter::RegisterEvent(std::uint32_t eventKey) {
    if (static_cast<capture::CharacterEventKey>(eventKey) !=
        capture::CharacterEventKey::kCharacterLevelChanged) {
        return false;
    }
    RE::LevelIncrease::GetEventSource()->AddEventSink(levelChangedEventSink_.get());
    return true;
}

} //  namespace dovahlink::adapter::runtime
