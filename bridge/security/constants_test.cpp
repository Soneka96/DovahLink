#include "security/constants.hpp"

#include <catch2/catch_test_macros.hpp>

using namespace dovahlink::security;

TEST_CASE("input limits match the documented Phase 1 values",
          "[security][constants]") {
    CHECK(kMaxInboundFrameBytes == 1024 * 1024);
    CHECK(kMaxJsonNestingDepth == 32);
    CHECK(kMaxStringLengthBytes == 4 * 1024);
    CHECK(kMaxArrayItems == 128);
    CHECK(kMaxObjectMembers == 64);
}

TEST_CASE("rate and session limits match the documented Phase 1 values",
          "[security][constants]") {
    CHECK(kMaxInboundMessagesPerSecond == 100);
    CHECK(kMaxMessagesPerSession == 10'000);
    CHECK(kMaxConnectedClients == 1);
}

TEST_CASE("timeouts match the documented Phase 1 values",
          "[security][constants]") {
    CHECK(kHandshakeTimeout == std::chrono::seconds{5});
    CHECK(kIdleTimeout == std::chrono::seconds{60});
}

TEST_CASE("outbound queue lanes match the documented Phase 1 values",
          "[security][constants]") {
    CHECK(kReservedControlRecoverySlots == 16);
    CHECK(kReservedEventSlots == 112);
    CHECK(kMaxOutboundQueueMessages == 128);
    CHECK(kMaxOutboundQueueMessages ==
          kReservedControlRecoverySlots + kReservedEventSlots);
}

TEST_CASE("throttling limits match the documented Phase 1 values",
          "[security][constants]") {
    CHECK(kMaxFailedTokenAttempts == 5);
    CHECK(kFailedTokenAttemptWindow == std::chrono::seconds{60});
    CHECK(kMaxProtocolViolations == 3);
    CHECK(kProtocolViolationWindow == std::chrono::seconds{30});
}

TEST_CASE("pairing challenge limits match the documented Phase 3.1 values",
          "[security][constants]") {
    CHECK(kPairingReconnectGracePeriod == std::chrono::seconds{10});
    CHECK(kPairingRenotifyCooldown == std::chrono::seconds{5});
    CHECK(kPairingConfirmPacingInterval == std::chrono::seconds{1});
    CHECK(kPairingMaxWrongAttempts == 5);
    CHECK(kPairingPendingCredentialTtl == std::chrono::minutes{5});
}
