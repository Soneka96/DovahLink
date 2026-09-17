#include "runtime/commonlib_adapter_native_capture_router.hpp"

#include <algorithm>
#include <array>
#include <iterator>

#include "capture/live_state_sample_codec.hpp"
#include "enums.hpp"
#include "runtime/commonlib_adapter_character_capture.hpp"

namespace dovahlink::adapter::runtime {

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

bool CommonLibAdapterNativeCaptureRouter::RegisterEvent(std::uint32_t /*eventKey*/) {
    return false;
}

} //  namespace dovahlink::adapter::runtime
