#pragma once

#include <chrono>
#include <cstddef>

namespace dovahlink::security {

// Phase 1 transport, session, and rate limits. See ai/context/protocol/security.md
// for the source of truth. Changing any value here requires a new direct
// maintainer approval and a documented reason (see TASK.md).

inline constexpr std::size_t kMaxInboundFrameBytes = 1024 * 1024;  // 1 MiB
inline constexpr int kMaxJsonNestingDepth = 32;
inline constexpr std::size_t kMaxStringLengthBytes = 4 * 1024;  // 4 KiB
inline constexpr std::size_t kMaxArrayItems = 128;
inline constexpr std::size_t kMaxObjectMembers = 64;

inline constexpr std::size_t kMaxInboundMessagesPerSecond = 100;
inline constexpr std::chrono::seconds kInboundMessageRateWindow{1};
inline constexpr std::size_t kMaxMessagesPerSession = 10'000;
inline constexpr std::size_t kMaxConnectedClients = 1;

inline constexpr std::chrono::seconds kHandshakeTimeout{5};
inline constexpr std::chrono::seconds kIdleTimeout{60};

inline constexpr std::size_t kReservedControlRecoverySlots = 16;
inline constexpr std::size_t kReservedEventSlots = 112;
inline constexpr std::size_t kMaxOutboundQueueMessages =
    kReservedControlRecoverySlots + kReservedEventSlots;

inline constexpr std::size_t kMaxFailedTokenAttempts = 5;
inline constexpr std::chrono::seconds kFailedTokenAttemptWindow{60};

inline constexpr std::size_t kMaxProtocolViolations = 3;
inline constexpr std::chrono::seconds kProtocolViolationWindow{30};

}  // namespace dovahlink::security
