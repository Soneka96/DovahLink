#pragma once

#include <chrono>
#include <cstddef>

namespace dovahlink::security {

///  Maximum inbound WebSocket frame size in bytes.
inline constexpr std::size_t kMaxInboundFrameBytes = 1024 * 1024;
///  Maximum JSON nesting depth accepted by the parser.
inline constexpr int kMaxJsonNestingDepth = 32;
///  Maximum JSON string or object-key length in bytes.
inline constexpr std::size_t kMaxStringLengthBytes = 4 * 1024;
///  Maximum number of items in a JSON array.
inline constexpr std::size_t kMaxArrayItems = 128;
///  Maximum number of members in a JSON object.
inline constexpr std::size_t kMaxObjectMembers = 64;

///  Maximum inbound messages accepted per rate window.
inline constexpr std::size_t kMaxInboundMessagesPerSecond = 100;
///  Sliding window used by the inbound message-rate limiter.
inline constexpr std::chrono::seconds kInboundMessageRateWindow{1};
///  Maximum inbound messages processed by one session.
inline constexpr std::size_t kMaxMessagesPerSession = 10'000;
///  Maximum simultaneously connected clients.
inline constexpr std::size_t kMaxConnectedClients = 1;

///  Time allowed for a client to complete the handshake.
inline constexpr std::chrono::seconds kHandshakeTimeout{5};
///  Idle timeout applied after authentication.
inline constexpr std::chrono::seconds kIdleTimeout{60};

///  Reserved control-lane slots for recovery and protocol responses.
inline constexpr std::size_t kReservedControlRecoverySlots = 16;
///  Reserved event-lane slots.
inline constexpr std::size_t kReservedEventSlots = 112;
///  Total outbound queue capacity across both lanes.
inline constexpr std::size_t kMaxOutboundQueueMessages =
    kReservedControlRecoverySlots + kReservedEventSlots;

///  Maximum failed token attempts before the next attempt is rejected outright.
inline constexpr std::size_t kMaxFailedTokenAttempts = 5;
///  Sliding window for failed token attempts.
inline constexpr std::chrono::seconds kFailedTokenAttemptWindow{60};

///  Number of protocol violations that closes a connection.
inline constexpr std::size_t kMaxProtocolViolations = 3;
///  Sliding window for protocol violations.
inline constexpr std::chrono::seconds kProtocolViolationWindow{30};

///  Maximum length of a trusted client's optional presentation-only display
///  name.
inline constexpr std::size_t kMaxDisplayNameLengthBytes = 64;
///  Maximum attempts to generate a unique trusted-client `shortId` before
///  failing closed.
inline constexpr std::size_t kMaxShortIdGenerationAttempts = 20;

///  How long an active pairing challenge survives its owning client's disconnect
///  before it is cancelled outright and freed for another device.
inline constexpr std::chrono::seconds kPairingReconnectGracePeriod{10};
///  Minimum interval between successful "show code again" redisplays of the same
///  active code.
inline constexpr std::chrono::seconds kPairingRenotifyCooldown{5};
///  Minimum interval between evaluated `pairing_confirm` code validations.
inline constexpr std::chrono::seconds kPairingConfirmPacingInterval{1};
///  Wrong evaluated codes allowed per challenge before it is cancelled outright.
inline constexpr std::size_t kPairingMaxWrongAttempts = 5;
///  How long a credential may remain pending finalization before it expires.
inline constexpr std::chrono::minutes kPairingPendingCredentialTtl{5};

///  Number of decimal digits in a Factory Reset confirmation code.
inline constexpr std::size_t kFactoryResetCodeDigits = 6;
///  How long a started Factory Reset confirmation code remains valid.
inline constexpr std::chrono::seconds kFactoryResetCodeTtl{60};

} //  namespace dovahlink::security
