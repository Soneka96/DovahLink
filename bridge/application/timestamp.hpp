#pragma once

#include <chrono>
#include <string>

namespace dovahlink::application {

// Formats `time` as UTC RFC 3339 wall-clock text, second precision (e.g.
// "2026-08-10T12:00:00Z"), matching protocol/schema/README.md's occurredAt
// field and protocol/fixtures' example values. occurredAt is documented as
// "not an ordering source", so sub-second precision is not needed for
// Phase 1. Takes `time` explicitly rather than reading the clock itself so
// callers can test deterministically (ai/context/skse/testing.md).
[[nodiscard]] std::string FormatTimestamp(std::chrono::system_clock::time_point time);

}  // namespace dovahlink::application
