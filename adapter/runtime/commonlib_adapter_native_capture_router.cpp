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
    LevelChangedEventSink(capture::IAdapterCaptureHandoffQueue& captureQueue,
                          identity::IAdapterPlayContextState& playContextState)
        : captureQueue_(captureQueue), playContextState_(playContextState) {}

    ///  Copies the new level and enqueues it, stamped with the current play
    ///  context. The queue may reject an item at capacity; unlike a rejected
    ///  Snapshot sample, this reliable Event's own loss is not left silently
    ///  handled here -- `TryEnqueue` itself reports the rejection to the
    ///  queue's own `onRejected` callback, which `AdapterRuntime`'s
    ///  composition resets the private IPC connection for, so a dropped
    ///  level-changed event can never leave continuity looking trustworthy
    ///  when it is not.
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
            .playContextId =
                playContextState_.CurrentPlayContext().value_or(
                    std::array<std::byte, 16>{}),
        });
        return RE::BSEventNotifyControl::kContinue;
    }

  private:
    capture::IAdapterCaptureHandoffQueue& captureQueue_;
    identity::IAdapterPlayContextState& playContextState_;
};

CommonLibAdapterNativeCaptureRouter::CommonLibAdapterNativeCaptureRouter(
    capture::IAdapterCaptureHandoffQueue& captureQueue,
    identity::IAdapterPlayContextState& playContextState)
    : captureQueue_(captureQueue), playContextState_(playContextState),
      levelChangedEventSink_(std::make_unique<LevelChangedEventSink>(
          captureQueue, playContextState)) {}

CommonLibAdapterNativeCaptureRouter::~CommonLibAdapterNativeCaptureRouter() = default;

dispatch::SampleCaptureResult
CommonLibAdapterNativeCaptureRouter::CaptureSample(std::uint32_t sampleToken) {
    switch (static_cast<capture::CharacterSampleToken>(sampleToken)) {
    case capture::CharacterSampleToken::kCharacterVitals: {
        std::optional<CharacterVitalsCapture> vitals = CaptureCharacterVitals();
        if (!vitals) {
            return dispatch::SampleCaptureResult{
                .status = dispatch::SampleCaptureStatus::kUnavailable};
        }
        std::vector<std::byte> payload;
        payload.reserve(12);
        std::array<std::byte, 4> health = capture::EncodeFloatLittleEndian(vitals->health);
        std::array<std::byte, 4> magicka = capture::EncodeFloatLittleEndian(vitals->magicka);
        std::array<std::byte, 4> stamina = capture::EncodeFloatLittleEndian(vitals->stamina);
        std::ranges::copy(health, std::back_inserter(payload));
        std::ranges::copy(magicka, std::back_inserter(payload));
        std::ranges::copy(stamina, std::back_inserter(payload));
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kAvailable,
            .payload = std::move(payload)};
    }
    case capture::CharacterSampleToken::kCharacterXp: {
        std::optional<float> xp = CaptureCharacterXp();
        if (!xp) {
            return dispatch::SampleCaptureResult{
                .status = dispatch::SampleCaptureStatus::kUnavailable};
        }
        std::array<std::byte, 4> encoded = capture::EncodeFloatLittleEndian(*xp);
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kAvailable,
            .payload = std::vector<std::byte>(encoded.begin(), encoded.end())};
    }
    case capture::CharacterSampleToken::kCharacterLevelBaseline: {
        std::optional<std::uint16_t> level = CaptureCharacterLevel();
        if (!level) {
            return dispatch::SampleCaptureResult{
                .status = dispatch::SampleCaptureStatus::kUnavailable};
        }
        std::array<std::byte, 2> encoded = capture::EncodeUInt16LittleEndian(*level);
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kAvailable,
            .payload = std::vector<std::byte>(encoded.begin(), encoded.end())};
    }
    default:
        return dispatch::SampleCaptureResult{
            .status = dispatch::SampleCaptureStatus::kUnsupported};
    }
}

bool CommonLibAdapterNativeCaptureRouter::RegisterEvent(std::uint32_t eventKey) {
    if (static_cast<capture::CharacterEventKey>(eventKey) !=
        capture::CharacterEventKey::kCharacterLevelChanged) {
        return false;
    }
    auto* source = RE::LevelIncrease::GetEventSource();
    if (source == nullptr) {
        return false;
    }
    source->AddEventSink(levelChangedEventSink_.get());
    return true;
}

} //  namespace dovahlink::adapter::runtime
