#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::application {

/// Owned application value for the currently captured player level, independent of whether any
/// wire-facing state area currently exposes it (protocol/schema/README.md's "Registered state
/// areas").
struct CharacterSnapshot {
    /// Captured player level, or no value when the runtime value is unavailable.
    std::optional<std::int64_t> level;
};

}  // namespace dovahlink::application
