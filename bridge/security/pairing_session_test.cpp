#include "security/pairing_session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cctype>
#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

using dovahlink::security::CancelOutcome;
using dovahlink::security::ConfirmResult;
using dovahlink::security::PairingSession;
using dovahlink::security::PendingCredential;
using dovahlink::security::RenotifyOutcome;
using dovahlink::security::StartChallengeOutcome;
using dovahlink::security::TrustMutationGeneration;

namespace {

///  Deterministic `PairingSession::CodeGenerator` that always returns `code`.
PairingSession::CodeGenerator FixedCode(std::string code) {
    return
        [code = std::move(code)]() -> std::optional<std::string> { return code; };
}

///  `PairingSession::CodeGenerator` that fails once (simulating a transient
///  CSPRNG failure), then returns `code` on every later call.
PairingSession::CodeGenerator FlakyCodeGenerator(std::string code) {
    auto failedOnce = std::make_shared<bool>(false);
    return [code = std::move(code), failedOnce]() -> std::optional<std::string> {
        if (!*failedOnce) {
            *failedOnce = true;
            return std::nullopt;
        }
        return code;
    };
}

///  Builds a deterministic credential-sized byte sequence from a seed value.
std::vector<std::uint8_t> MakeCredential(std::uint8_t seed) {
    return std::vector<std::uint8_t>{seed, static_cast<std::uint8_t>(seed + 1)};
}

} //  namespace

TEST_CASE(
    "full pairing lifecycle: start, confirm, peek then commit returns to NONE",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));
    auto now = std::chrono::steady_clock::now();

    auto result = session.TryStartChallenge("client-1", now);
    REQUIRE(result.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(result.code.has_value());
    CHECK(*result.code == "123456");

    REQUIRE(session
                .TryConfirmCode("123456", now, "client-1", MakeCredential(1),
                                std::string("My PC"), TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto peeked = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(peeked.has_value());
    CHECK(peeked->clientId == "client-1");
    CHECK(peeked->credential == MakeCredential(1));
    REQUIRE(peeked->displayName.has_value());
    CHECK(*peeked->displayName == "My PC");

    REQUIRE(session.CommitPending("client-1", MakeCredential(1), now));

    //  Back to NONE: a fresh challenge can start again.
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("pending credentials retain their captured trust mutation generation",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));
    auto now = std::chrono::steady_clock::now();
    constexpr TrustMutationGeneration generation{.global = 4, .client = 7};

    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("123456", now, "client-1", MakeCredential(1),
                                std::nullopt, generation)
                .outcome == ConfirmResult::kConfirmed);

    auto pending = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(pending.has_value());
    CHECK(pending->mutationGeneration == generation);
}

TEST_CASE("RestorePending makes a consumed credential retryable",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("123456", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto pending = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(pending.has_value());
    REQUIRE(session.CommitPending("client-1", MakeCredential(1), now));
    REQUIRE(session.RestorePending(std::move(*pending)));

    CHECK(session.PeekPending("client-1", MakeCredential(1), now).has_value());
}

TEST_CASE("RestorePending refuses to replace an occupied pending credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("123456", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);
    auto original = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(original.has_value());
    REQUIRE(session.CommitPending("client-1", MakeCredential(1), now));

    REQUIRE(session.TryStartChallenge("client-2", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("123456", now, "client-2", MakeCredential(2),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.RestorePending(std::move(*original)));
    auto occupied = session.PeekPending("client-2", MakeCredential(2), now);
    REQUIRE(occupied.has_value());
    CHECK(occupied->clientId == "client-2");
}

TEST_CASE("a second TryStartChallenge from the owning client resumes without a "
          "new code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto resumed = session.TryStartChallenge("client-1", now);
    CHECK(resumed.outcome == StartChallengeOutcome::kResumed);
    CHECK_FALSE(resumed.code.has_value());

    //  The original code still works -- it was not replaced or cleared.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a second TryStartChallenge from a different client reports "
          "kOtherDeviceActive and "
          "reveals nothing",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto busy = session.TryStartChallenge("client-2", now);
    CHECK(busy.outcome == StartChallengeOutcome::kOtherDeviceActive);
    CHECK_FALSE(busy.code.has_value());

    //  client-1's original code is untouched.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a non-owner cannot confirm another client's active code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK(session
              .TryConfirmCode("111111", now, "client-2", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);

    //  The real owner's correct code still works afterward -- the wrong-owner
    //  attempt did not consume or invalidate it.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE(
    "TryStartChallenge resumes for the pending credential's owner, and reports "
    "kOtherDeviceActive for anyone else",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kResumed);
    CHECK(session.TryStartChallenge("client-2", now).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("a disconnect followed by reconnect within the grace period resumes "
          "the owned challenge",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    auto reconnectTime = now + std::chrono::seconds(5);
    session.NotifyReconnected("client-1", reconnectTime);

    //  Still owned by client-1, and another device is still shut out.
    CHECK(session.TryStartChallenge("client-1", reconnectTime).outcome ==
          StartChallengeOutcome::kResumed);
    CHECK(session.TryStartChallenge("client-2", reconnectTime).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);
    CHECK(session
              .TryConfirmCode("111111", reconnectTime, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE(
    "a disconnect past the grace period frees the challenge for another device",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    auto pastGrace = now + std::chrono::seconds(11);

    auto started = session.TryStartChallenge("client-2", pastGrace);
    CHECK(started.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(started.code.has_value());

    //  client-1's stale reconnect no longer resumes anything -- client-2 now owns
    //  the slot.
    CHECK(session.TryStartChallenge("client-1", pastGrace).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("a disconnect does not start the grace countdown for a non-owner",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  client-2 never owned anything; this must not affect client-1's challenge.
    session.NotifyDisconnected("client-2", now);

    auto pastWhatWouldHaveBeenGrace = now + std::chrono::seconds(11);
    CHECK(session.TryStartChallenge("client-1", pastWhatWouldHaveBeenGrace)
              .outcome == StartChallengeOutcome::kResumed);
}

TEST_CASE("NotifyDisconnected does not start the grace countdown once "
          "PENDING_CREDENTIAL",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    //  The owner "disconnects" while the credential is only pending -- the grace
    //  period does not apply here, so this must not clear or otherwise disturb the
    //  pending credential.
    session.NotifyDisconnected("client-1", now);

    auto pastWhatWouldHaveBeenGrace = now + std::chrono::seconds(11);
    CHECK(session
              .PeekPending("client-1", MakeCredential(1),
                           pastWhatWouldHaveBeenGrace)
              .has_value());
    CHECK(session.TryStartChallenge("client-1", pastWhatWouldHaveBeenGrace)
              .outcome == StartChallengeOutcome::kResumed);
}

TEST_CASE(
    "a disconnect exactly at the grace-period boundary is treated as elapsed",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    auto exactlyAtGrace = now + std::chrono::seconds(10);

    CHECK(session.TryStartChallenge("client-2", exactlyAtGrace).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("RemainingSeconds at the grace-period boundary reports elapsed",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    auto exactlyAtGrace = now + std::chrono::seconds(10);

    CHECK_FALSE(session.RemainingSeconds(exactlyAtGrace).has_value());
}

TEST_CASE("RemainingSeconds reports no value once the challenge reaches "
          "PENDING_CREDENTIAL",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.RemainingSeconds(now).has_value());
}

TEST_CASE("RemainingSeconds reports no value with no active challenge",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(
        session.RemainingSeconds(std::chrono::steady_clock::now()).has_value());
}

TEST_CASE("CurrentCode returns the active challenge's own code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto code = session.CurrentCode(now);
    REQUIRE(code.has_value());
    CHECK(*code == "111111");
}

TEST_CASE("CurrentCode reports no value with no active challenge",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(
        session.CurrentCode(std::chrono::steady_clock::now()).has_value());
}

TEST_CASE("CurrentCode reports no value once the challenge reaches "
          "PENDING_CREDENTIAL",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.CurrentCode(now).has_value());
}

TEST_CASE("CurrentCode reports no value once the challenge's own TTL naturally "
          "elapses, with no "
          "disconnect involved",
          "[security][pairing_session]") {
    //  Zero TTL: the challenge is already past its own expiry the instant it
    //  starts, distinct from the reconnect-grace path (the owner never disconnects
    //  here).
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK_FALSE(session.CurrentCode(now).has_value());
}

TEST_CASE("CurrentCode respects the reconnect-grace lazy-expiry check",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    CHECK_FALSE(session.CurrentCode(now + std::chrono::seconds(11)).has_value());
}

TEST_CASE("NotifyReconnected from a non-owner does not clear the real owner's "
          "grace countdown",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyDisconnected("client-1", now);
    session.NotifyReconnected("client-2", now);

    //  client-1's countdown is untouched by client-2's reconnect -- past grace,
    //  the slot still frees up on schedule.
    auto pastGrace = now + std::chrono::seconds(11);
    CHECK(session.TryStartChallenge("client-2", pastGrace).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("NotifyReconnected with no prior disconnect is a harmless no-op",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.NotifyReconnected("client-1", now);

    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kResumed);
}

TEST_CASE("a non-owner cannot confirm a code while a different client's "
          "credential is pending",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    //  No active challenge remains once PENDING_CREDENTIAL -- a second client's
    //  confirm attempt finds nothing to confirm, and client-1's pending credential
    //  is undisturbed.
    CHECK(session
              .TryConfirmCode("111111", now, "client-2", MakeCredential(2),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
    CHECK(session.PeekPending("client-1", MakeCredential(1), now).has_value());
}

TEST_CASE("TryConfirmCode reports kInvalid with no active challenge",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK(session
              .TryConfirmCode("111111", std::chrono::steady_clock::now(),
                              "client-1", MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
}

TEST_CASE("a wrong code does not consume the real one, which still succeeds "
          "afterward",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK(session
              .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
    //  Spaced past the pacing interval so this second attempt is actually
    //  evaluated.
    auto later = now + std::chrono::seconds(1);
    CHECK(session
              .TryConfirmCode("111111", later, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a code cannot be confirmed twice", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    //  The challenge was consumed and cleared -- a second attempt finds no active
    //  challenge at all, distinct from a code that exists but expired.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
}

TEST_CASE("TryConfirmCode reports kExpired for an expired code, distinct from "
          "kInvalid",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kExpired);
}

TEST_CASE("an evaluated attempt within the pacing interval is rejected without "
          "counting as wrong",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  First attempt (wrong) is evaluated and starts the pacing clock.
    REQUIRE(session
                .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kInvalid);
    //  Immediately retrying with the correct code, still within the pacing
    //  interval, is paced out rather than evaluated -- proving it never reaches
    //  the comparison at all.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kPacingLimited);

    //  Once the pacing interval has passed, the correct code is evaluated and
    //  succeeds -- the paced-out attempt above never consumed a wrong-attempt
    //  slot.
    auto pastPacing = now + std::chrono::seconds(1);
    CHECK(session
              .TryConfirmCode("111111", pastPacing, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("kPacingLimited reports the actual remaining wait, not the full "
          "pacing interval",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  First attempt (wrong) is evaluated and starts the pacing clock.
    REQUIRE(session
                .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kInvalid);

    //  400ms later: well past the pacing interval's start, so the true remaining
    //  wait (600ms) rounds up to the minimum safe whole-second wait of 1 second.
    auto paced =
        session.TryConfirmCode("111111", now + std::chrono::milliseconds(400),
                               "client-1", MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(paced.outcome == ConfirmResult::kPacingLimited);
    REQUIRE(paced.retryAfterSeconds.has_value());
    CHECK(*paced.retryAfterSeconds == std::chrono::seconds(1));
}

TEST_CASE(
    "TryRenotify reports a safe whole-second wait for fractional cooldowns",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session.TryRenotify("client-1", now).outcome ==
            RenotifyOutcome::kRenotified);

    auto cooldown =
        session.TryRenotify("client-1", now + std::chrono::milliseconds(1));
    REQUIRE(cooldown.outcome == RenotifyOutcome::kCooldown);
    REQUIRE(cooldown.retryAfterSeconds.has_value());
    CHECK(*cooldown.retryAfterSeconds == std::chrono::seconds(5));
    CHECK(
        session.TryRenotify("client-1", now + std::chrono::seconds(5)).outcome ==
        RenotifyOutcome::kRenotified);
}

TEST_CASE("the fifth wrong evaluated attempt cancels the challenge and "
          "destroys the code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Four wrong attempts, spaced past the pacing interval so each is genuinely
    //  evaluated.
    for (int i = 0; i < 4; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        CHECK(session
                  .TryConfirmCode("000000", attemptTime, "client-1",
                                  MakeCredential(1), std::nullopt, TrustMutationGeneration{})
                  .outcome == ConfirmResult::kInvalid);
    }

    auto fifthAttemptTime = now + std::chrono::seconds(4);
    CHECK(session
              .TryConfirmCode("000000", fifthAttemptTime, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kHardLimitReached);

    //  The now-destroyed code no longer works, even presented correctly, and even
    //  the real owner must start an entirely new challenge.
    auto afterCancel = now + std::chrono::seconds(5);
    CHECK(session
              .TryConfirmCode("111111", afterCancel, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
    CHECK(session.TryStartChallenge("client-1", afterCancel).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("shouldAutoRenotify is true for the first wrong attempt and false "
          "for one within its own "
          "cooldown, independent of pacing",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto first = session.TryConfirmCode("000000", now, "client-1",
                                        MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(first.outcome == ConfirmResult::kInvalid);
    CHECK(first.shouldAutoRenotify);

    //  One second later clears pacing but not the 5-second auto-renotify cooldown.
    auto secondAttemptTime = now + std::chrono::seconds(1);
    auto second = session.TryConfirmCode("000000", secondAttemptTime, "client-1",
                                         MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(second.outcome == ConfirmResult::kInvalid);
    CHECK_FALSE(second.shouldAutoRenotify);

    //  Past the 5-second auto-renotify cooldown, a wrong attempt fires it again.
    auto thirdAttemptTime = now + std::chrono::seconds(6);
    auto third = session.TryConfirmCode("000000", thirdAttemptTime, "client-1",
                                        MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(third.outcome == ConfirmResult::kInvalid);
    CHECK(third.shouldAutoRenotify);
}

TEST_CASE("shouldAutoRenotify is always false for kConfirmed, kExpired, "
          "kPacingLimited, and "
          "kHardLimitReached",
          "[security][pairing_session]") {
    auto now = std::chrono::steady_clock::now();

    PairingSession confirmed(FixedCode("111111"));
    REQUIRE(confirmed.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    auto confirmedResult = confirmed.TryConfirmCode(
        "111111", now, "client-1", MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(confirmedResult.outcome == ConfirmResult::kConfirmed);
    CHECK_FALSE(confirmedResult.shouldAutoRenotify);

    PairingSession expired(FixedCode("111111"), std::chrono::seconds(0));
    REQUIRE(expired.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    auto expiredResult = expired.TryConfirmCode("111111", now, "client-1",
                                                MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(expiredResult.outcome == ConfirmResult::kExpired);
    CHECK_FALSE(expiredResult.shouldAutoRenotify);

    PairingSession paced(FixedCode("111111"));
    REQUIRE(paced.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(paced
                .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kInvalid);
    auto pacedResult = paced.TryConfirmCode("000000", now, "client-1",
                                            MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(pacedResult.outcome == ConfirmResult::kPacingLimited);
    CHECK_FALSE(pacedResult.shouldAutoRenotify);

    PairingSession hardLimited(FixedCode("111111"));
    REQUIRE(hardLimited.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    for (int i = 0; i < 4; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        REQUIRE(hardLimited
                    .TryConfirmCode("000000", attemptTime, "client-1",
                                    MakeCredential(1), std::nullopt, TrustMutationGeneration{})
                    .outcome == ConfirmResult::kInvalid);
    }
    auto hardLimitResult =
        hardLimited.TryConfirmCode("000000", now + std::chrono::seconds(4),
                                   "client-1", MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(hardLimitResult.outcome == ConfirmResult::kHardLimitReached);
    CHECK_FALSE(hardLimitResult.shouldAutoRenotify);
}

TEST_CASE("TryRenotify reports kNotActive with nothing owned",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    auto result =
        session.TryRenotify("client-1", std::chrono::steady_clock::now());
    CHECK(result.outcome == RenotifyOutcome::kNotActive);
    CHECK_FALSE(result.retryAfterSeconds.has_value());
}

TEST_CASE("TryRenotify reports kNotActive for a non-owner",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto result = session.TryRenotify("client-2", now);
    CHECK(result.outcome == RenotifyOutcome::kNotActive);
}

TEST_CASE("TryRenotify succeeds for the owner, then cooldowns, then succeeds "
          "again after the "
          "cooldown elapses",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    auto first = session.TryRenotify("client-1", now);
    CHECK(first.outcome == RenotifyOutcome::kRenotified);

    auto second = session.TryRenotify("client-1", now + std::chrono::seconds(2));
    REQUIRE(second.outcome == RenotifyOutcome::kCooldown);
    REQUIRE(second.retryAfterSeconds.has_value());
    CHECK(*second.retryAfterSeconds == std::chrono::seconds(3));

    auto third = session.TryRenotify("client-1", now + std::chrono::seconds(5));
    CHECK(third.outcome == RenotifyOutcome::kRenotified);
}

TEST_CASE("TryRenotify's cooldown is independent of the auto-renotify cooldown",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  A wrong attempt fires the auto-renotify cooldown; the manual "show again"
    //  cooldown must be untouched by it.
    auto wrong = session.TryConfirmCode("000000", now, "client-1",
                                        MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(wrong.outcome == ConfirmResult::kInvalid);
    REQUIRE(wrong.shouldAutoRenotify);
    CHECK(session.TryRenotify("client-1", now).outcome ==
          RenotifyOutcome::kRenotified);

    //  The manual renotify above must not affect the auto-renotify cooldown
    //  either: a wrong attempt one pacing interval later still respects its own
    //  (already-active) cooldown from the first wrong attempt, not a fresh one
    //  seeded by the manual renotify call.
    auto secondWrongTime = now + std::chrono::seconds(1);
    auto secondWrong = session.TryConfirmCode(
        "000000", secondWrongTime, "client-1", MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(secondWrong.outcome == ConfirmResult::kInvalid);
    CHECK_FALSE(secondWrong.shouldAutoRenotify);
}

TEST_CASE("TryCancel reports kAlreadyIdle with nothing owned",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK(session.TryCancel("client-1", std::chrono::steady_clock::now()) ==
          CancelOutcome::kAlreadyIdle);
}

TEST_CASE("TryCancel reports kAlreadyIdle for a non-owner and does not disturb "
          "the real owner",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK(session.TryCancel("client-2", now) == CancelOutcome::kAlreadyIdle);
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE(
    "TryCancel clears an owned active challenge and frees it for a fresh start",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    CHECK(session.TryCancel("client-1", now) == CancelOutcome::kCancelled);
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE(
    "TryCancel clears an owned pending credential without touching TrustStore",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK(session.TryCancel("client-1", now) == CancelOutcome::kCancelled);
    CHECK_FALSE(
        session.PeekPending("client-1", MakeCredential(1), now).has_value());
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE(
    "TryCancel is idempotent: repeating it reports kAlreadyIdle truthfully",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    REQUIRE(session.TryCancel("client-1", now) == CancelOutcome::kCancelled);
    CHECK(session.TryCancel("client-1", now) == CancelOutcome::kAlreadyIdle);
}

TEST_CASE("TryStartChallenge issues a fresh code once the previous one expired",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kExpired);

    auto retried = session.TryStartChallenge("client-1", now);

    REQUIRE(retried.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(retried.code.has_value());
    CHECK(*retried.code == "111111");
}

TEST_CASE("TryStartChallenge reports kGeneratorFailed distinctly from "
          "kResumed, and can retry "
          "after the failure",
          "[security][pairing_session]") {
    PairingSession session(FlakyCodeGenerator("111111"));
    auto now = std::chrono::steady_clock::now();

    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kGeneratorFailed);

    auto retried = session.TryStartChallenge("client-1", now);
    REQUIRE(retried.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(retried.code.has_value());
    CHECK(*retried.code == "111111");
}

TEST_CASE(
    "a pending credential expires after 5 minutes and is treated as absent",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto pastTtl = now + std::chrono::minutes(5);
    CHECK_FALSE(
        session.PeekPending("client-1", MakeCredential(1), pastTtl).has_value());
    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(1), pastTtl));
}

TEST_CASE("an expired pending credential no longer blocks a fresh challenge or "
          "reports kResumed",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto pastTtl = now + std::chrono::minutes(5);
    auto started = session.TryStartChallenge("client-1", pastTtl);
    CHECK(started.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(started.code.has_value());
}

TEST_CASE("an expired pending credential is not cancellable -- TryCancel "
          "reports kAlreadyIdle",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto pastTtl = now + std::chrono::minutes(5);
    CHECK(session.TryCancel("client-1", pastTtl) == CancelOutcome::kAlreadyIdle);
}

TEST_CASE("PeekPending fails with no pending credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(session
                    .PeekPending("client-1", MakeCredential(1),
                                 std::chrono::steady_clock::now())
                    .has_value());
}

TEST_CASE("PeekPending fails and leaves the pending credential intact for the "
          "wrong clientId",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(
        session.PeekPending("client-2", MakeCredential(1), now).has_value());
    //  The correct clientId+credential still peeks afterward.
    CHECK(session.PeekPending("client-1", MakeCredential(1), now).has_value());
}

TEST_CASE("PeekPending fails and leaves the pending credential intact for the "
          "wrong credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(
        session.PeekPending("client-1", MakeCredential(9), now).has_value());
    CHECK(session.PeekPending("client-1", MakeCredential(1), now).has_value());
}

TEST_CASE("PeekPending does not consume the pending credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto first = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(first.has_value());
    CHECK_FALSE(first->displayName.has_value());

    //  A second peek still finds it, with identical contents -- peeking alone must
    //  be safe to repeat (for example across a failed TrustStore::Persist and its
    //  retry) and must not mutate what it returns.
    auto second = session.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(second.has_value());
    CHECK(second->clientId == first->clientId);
    CHECK(second->credential == first->credential);
    CHECK(second->displayName == first->displayName);
}

TEST_CASE("PeekPending is safe to repeat without CommitPending, and a later "
          "CommitPending still "
          "succeeds",
          "[security][pairing_session]") {
    //  Tests PairingSession's own Peek/Commit primitives in isolation, not a
    //  particular caller's ordering of them: PeekPending never consumes, so
    //  repeating it (for example across a caller-side retry loop) is always safe,
    //  and the pending credential remains committable until something --
    //  CommitPending or CancelAll -- actually consumes it.
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    REQUIRE(session.PeekPending("client-1", MakeCredential(1), now).has_value());

    //  Still PENDING_CREDENTIAL: a new challenge attempt from the same client just
    //  resumes it.
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kResumed);

    //  A second peek still finds it, and this time the caller commits.
    REQUIRE(session.PeekPending("client-1", MakeCredential(1), now).has_value());
    CHECK(session.CommitPending("client-1", MakeCredential(1), now));
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("CommitPending fails with no pending credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(1),
                                      std::chrono::steady_clock::now()));
}

TEST_CASE("CommitPending fails and leaves the pending credential intact for "
          "the wrong clientId",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.CommitPending("client-2", MakeCredential(1), now));
    //  The correct clientId+credential still commits afterward.
    CHECK(session.CommitPending("client-1", MakeCredential(1), now));
}

TEST_CASE("CommitPending fails and leaves the pending credential intact for "
          "the wrong credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(9), now));
    CHECK(session.CommitPending("client-1", MakeCredential(1), now));
}

TEST_CASE("CommitPending consumes the pending credential exactly once",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);
    REQUIRE(session.CommitPending("client-1", MakeCredential(1), now));

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(1), now));
    //  Nothing left to peek either, once committed.
    CHECK_FALSE(
        session.PeekPending("client-1", MakeCredential(1), now).has_value());
}

TEST_CASE("TryRenotify reports kNotActive once the challenge reaches "
          "PENDING_CREDENTIAL",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    //  Nothing left to redisplay -- the code is gone, only a pending credential
    //  remains.
    CHECK(session.TryRenotify("client-1", now).outcome ==
          RenotifyOutcome::kNotActive);
}

TEST_CASE("TryRenotify reports kNotActive once the challenge's own TTL "
          "naturally elapses, with no "
          "disconnect involved",
          "[security][pairing_session]") {
    //  Zero TTL: the challenge is already past its own expiry the instant it
    //  starts, distinct from the reconnect-grace path (the owner never disconnects
    //  here).
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  A dead code must never be reported as successfully redisplayed.
    CHECK(session.TryRenotify("client-1", now).outcome ==
          RenotifyOutcome::kNotActive);
}

TEST_CASE("TryCancel reports kAlreadyIdle, not kCancelled, once the "
          "challenge's own TTL naturally "
          "elapses",
          "[security][pairing_session]") {
    //  Zero TTL: the challenge is already past its own expiry the instant it
    //  starts, distinct from the reconnect-grace path (the owner never disconnects
    //  here).
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Nothing genuinely active was cleared -- truthfully reports kAlreadyIdle,
    //  matching TryCancel's own "idempotent without pretending work occurred"
    //  contract.
    CHECK(session.TryCancel("client-1", now) == CancelOutcome::kAlreadyIdle);
}

TEST_CASE(
    "an evaluated attempt exactly at the pacing-interval boundary is allowed",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    REQUIRE(session
                .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kInvalid);

    //  Just under the 1-second pacing interval is still paced out.
    auto justUnder = now + std::chrono::milliseconds(999);
    CHECK(session
              .TryConfirmCode("111111", justUnder, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kPacingLimited);

    //  Exactly at the boundary, the attempt is evaluated.
    auto exactlyAtBoundary = now + std::chrono::seconds(1);
    CHECK(session
              .TryConfirmCode("111111", exactlyAtBoundary, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a pending credential just under the 5-minute TTL is still valid",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    auto justUnderTtl = now + std::chrono::minutes(5) - std::chrono::seconds(1);
    CHECK(session.PeekPending("client-1", MakeCredential(1), justUnderTtl)
              .has_value());
    CHECK(session.CommitPending("client-1", MakeCredential(1), justUnderTtl));
}

TEST_CASE("the fifth wrong attempt cancels the challenge even mid "
          "reconnect-grace countdown",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  The owner's connection drops mid-attempt-sequence, but the grace period
    //  (10s) has not elapsed by the time the remaining attempts land.
    session.NotifyDisconnected("client-1", now);

    for (int i = 0; i < 4; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        CHECK(session
                  .TryConfirmCode("000000", attemptTime, "client-1",
                                  MakeCredential(1), std::nullopt, TrustMutationGeneration{})
                  .outcome == ConfirmResult::kInvalid);
    }
    auto fifthAttemptTime = now + std::chrono::seconds(4);
    CHECK(session
              .TryConfirmCode("000000", fifthAttemptTime, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kHardLimitReached);

    //  The slot is genuinely free now, for anyone, immediately -- not still
    //  "owned" until the grace period would have separately elapsed.
    CHECK(session.TryStartChallenge("client-2", fifthAttemptTime).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("a non-owner's rejected attempts never affect the real owner's "
          "wrong-attempt count or "
          "pacing clock",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Far more than the hard limit's worth of non-owner attempts, all at the same
    //  instant (which would itself be paced if it touched the owner's pacing
    //  clock).
    for (int i = 0; i < 10; ++i) {
        CHECK(session
                  .TryConfirmCode("111111", now, "client-2", MakeCredential(2),
                                  std::nullopt, TrustMutationGeneration{})
                  .outcome == ConfirmResult::kInvalid);
    }

    //  The real owner's first-ever evaluated attempt still succeeds immediately,
    //  unpaced and with a full wrong-attempt budget.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a fresh challenge resets the wrong-attempt count instead of "
          "carrying it over",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Four wrong attempts -- one short of the five-attempt hard limit.
    for (int i = 0; i < 4; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        CHECK(session
                  .TryConfirmCode("000000", attemptTime, "client-1",
                                  MakeCredential(1), std::nullopt, TrustMutationGeneration{})
                  .outcome == ConfirmResult::kInvalid);
    }
    auto restartTime = now + std::chrono::seconds(4);
    REQUIRE(session.TryCancel("client-1", restartTime) ==
            CancelOutcome::kCancelled);
    REQUIRE(session.TryStartChallenge("client-1", restartTime).outcome ==
            StartChallengeOutcome::kStarted);

    //  If the old count had carried over, this single wrong attempt against the
    //  fresh challenge would already be the "fifth" and cancel it. It must not.
    CHECK(session
              .TryConfirmCode("000000", restartTime, "client-1",
                              MakeCredential(1), std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kInvalid);
    auto later = restartTime + std::chrono::seconds(1);
    CHECK(session
              .TryConfirmCode("111111", later, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE(
    "a fresh challenge resets the pacing clock instead of carrying it over",
    "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    //  Evaluated attempt, pacing the old challenge's clock to `now`.
    REQUIRE(session
                .TryConfirmCode("000000", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kInvalid);

    REQUIRE(session.TryCancel("client-1", now) == CancelOutcome::kCancelled);
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  If the old pacing clock had carried over, this immediate attempt at the
    //  same `now` would be paced out. It must not be -- it is the fresh
    //  challenge's first-ever evaluated attempt.
    CHECK(session
              .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                              std::nullopt, TrustMutationGeneration{})
              .outcome == ConfirmResult::kConfirmed);
}

TEST_CASE("a fresh challenge resets both renotify cooldowns instead of "
          "carrying them over",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Fire both the manual and the auto-renotify cooldowns on the old challenge.
    REQUIRE(session.TryRenotify("client-1", now).outcome ==
            RenotifyOutcome::kRenotified);
    auto wrong = session.TryConfirmCode("000000", now, "client-1",
                                        MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(wrong.outcome == ConfirmResult::kInvalid);
    REQUIRE(wrong.shouldAutoRenotify);

    REQUIRE(session.TryCancel("client-1", now) == CancelOutcome::kCancelled);
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  If either cooldown had carried over, these would report kCooldown /
    //  shouldAutoRenotify == false at this same `now`. Neither must.
    CHECK(session.TryRenotify("client-1", now).outcome ==
          RenotifyOutcome::kRenotified);
    auto freshWrong = session.TryConfirmCode("000000", now, "client-1",
                                             MakeCredential(1), std::nullopt, TrustMutationGeneration{});
    REQUIRE(freshWrong.outcome == ConfirmResult::kInvalid);
    CHECK(freshWrong.shouldAutoRenotify);
}

TEST_CASE("DefaultCodeGenerator produces a six-digit numeric candidate",
          "[security][pairing_session]") {
    auto code = PairingSession::DefaultCodeGenerator();

    REQUIRE(code.has_value());
    CHECK(code->size() == 6);
    for (char ch : *code) {
        CHECK(std::isdigit(static_cast<unsigned char>(ch)));
    }
}

TEST_CASE("CancelAll clears an active challenge regardless of who owns it",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    session.CancelAll();

    //  The old owner's ownership is gone -- a fresh challenge starts, not a
    //  resume.
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("CancelAll clears a pending credential regardless of who owns it",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);
    REQUIRE(session
                .TryConfirmCode("111111", now, "client-1", MakeCredential(1),
                                std::nullopt, TrustMutationGeneration{})
                .outcome == ConfirmResult::kConfirmed);

    session.CancelAll();

    CHECK_FALSE(
        session.PeekPending("client-1", MakeCredential(1), now).has_value());
    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}

TEST_CASE("CancelAll is a harmless no-op when nothing is active",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    session.CancelAll();

    CHECK(session.TryStartChallenge("client-1", std::chrono::steady_clock::now())
              .outcome == StartChallengeOutcome::kStarted);
}

TEST_CASE("CancelAll needs no expiry check: clearing an already-expired "
          "challenge is harmless",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    auto now = std::chrono::steady_clock::now();
    REQUIRE(session.TryStartChallenge("client-1", now).outcome ==
            StartChallengeOutcome::kStarted);

    //  Unlike TryCancel, CancelAll performs no lazy-expiry check before clearing
    //  -- it must not misbehave when the challenge it is clearing already expired.
    session.CancelAll();

    CHECK(session.TryStartChallenge("client-1", now).outcome ==
          StartChallengeOutcome::kStarted);
}
