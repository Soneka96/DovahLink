#pragma once

#include <array>
#include <cstddef>

#include "capture/captured_payload.hpp"

namespace dovahlink::adapter::capture {

///  One complete observation of the independent Character supernatural
///  status and transformation-capability predicates.
struct CharacterSupernaturalTraitsCapture {
    ///  Whether the PlayerIsVampire global is nonzero.
    bool isVampire = false;
    ///  Whether the player possesses Dawnguard's Vampire Lord spell.
    bool hasVampireLordForm = false;
    ///  Whether the player possesses the canonical Beast Form spell.
    bool hasWerewolfForm = false;

    ///  Structural equality over every field.
    bool operator==(const CharacterSupernaturalTraitsCapture&) const = default;
};

///  Encodes the independent fields as three bytes in public contract order;
///  each byte is exactly 0 or 1.
///  @param traits The complete supernatural-traits observation.
///  @return The three-byte private payload.
inline CapturedPayload EncodeCharacterSupernaturalTraitsPayload(
    const CharacterSupernaturalTraitsCapture& traits) {
    const std::array<std::byte, 3> bytes{
        static_cast<std::byte>(traits.isVampire ? 1 : 0),
        static_cast<std::byte>(traits.hasVampireLordForm ? 1 : 0),
        static_cast<std::byte>(traits.hasWerewolfForm ? 1 : 0),
    };
    return MakeCapturedPayload(bytes);
}

} //  namespace dovahlink::adapter::capture
