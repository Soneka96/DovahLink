#include "security/pairing_session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cctype>
#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

using dovahlink::security::PairingSession;
using dovahlink::security::PendingCredential;
using ConfirmResult = dovahlink::security::PairingSession::ConfirmResult;
using StartChallengeOutcome = dovahlink::security::PairingSession::StartChallengeOutcome;

namespace {

/// Deterministic `PairingSession::CodeGenerator` that always returns `code`.
PairingSession::CodeGenerator FixedCode(std::string code) {
    return [code = std::move(code)]() -> std::optional<std::string> { return code; };
}

/// `PairingSession::CodeGenerator` that fails once (simulating a transient CSPRNG failure), then
/// returns `code` on every later call.
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

/// Builds a deterministic credential-sized byte sequence from a seed value.
std::vector<std::uint8_t> MakeCredential(std::uint8_t seed) {
    return std::vector<std::uint8_t>{seed, static_cast<std::uint8_t>(seed + 1)};
}

}  // namespace

TEST_CASE("full pairing lifecycle: start, confirm, peek then commit returns to NONE",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));

    auto result = session.TryStartChallenge();
    REQUIRE(result.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(result.code.has_value());
    CHECK(*result.code == "123456");

    REQUIRE(session.TryConfirmCode("123456", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::string("My PC")) == ConfirmResult::kConfirmed);

    auto peeked = session.PeekPending("client-1", MakeCredential(1));
    REQUIRE(peeked.has_value());
    CHECK(peeked->clientId == "client-1");
    CHECK(peeked->credential == MakeCredential(1));
    REQUIRE(peeked->displayName.has_value());
    CHECK(*peeked->displayName == "My PC");

    REQUIRE(session.CommitPending("client-1", MakeCredential(1)));

    // Back to NONE: a fresh challenge can start again.
    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
}

TEST_CASE("a second TryStartChallenge while CHALLENGE_ACTIVE does not replace the code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);

    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kAlreadyInProgress);

    // The original code still works -- it was not replaced or cleared.
    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kConfirmed);
}

TEST_CASE("TryStartChallenge fails while a credential is pending finalization",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kAlreadyInProgress);
}

TEST_CASE("TryConfirmCode reports kInvalid with no active challenge", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kInvalid);
}

TEST_CASE("a wrong code does not consume the real one, which still succeeds afterward",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);

    CHECK(session.TryConfirmCode("000000", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kInvalid);
    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kConfirmed);
}

TEST_CASE("a code cannot be confirmed twice", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    // The challenge was consumed and cleared -- a second attempt finds no active challenge at
    // all, distinct from a code that exists but expired.
    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kInvalid);
}

TEST_CASE("TryConfirmCode reports kExpired for an expired code, distinct from kInvalid",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);

    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kExpired);
}

TEST_CASE("repeated wrong codes block further attempts with kRateLimited", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);

    auto now = std::chrono::steady_clock::now();
    constexpr int kMaxFailedAttempts = 5;
    for (int i = 0; i < kMaxFailedAttempts; ++i) {
        CHECK(session.TryConfirmCode("000000", now, "client-1", MakeCredential(1), std::nullopt) ==
              ConfirmResult::kInvalid);
    }

    // Blocked now, even with the actually-correct code.
    CHECK(session.TryConfirmCode("111111", now, "client-1", MakeCredential(1), std::nullopt) ==
          ConfirmResult::kRateLimited);
}

TEST_CASE("attempts succeed again once the throttle window has passed",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);

    auto now = std::chrono::steady_clock::now();
    constexpr int kMaxFailedAttempts = 5;
    for (int i = 0; i < kMaxFailedAttempts; ++i) {
        CHECK(session.TryConfirmCode("000000", now, "client-1", MakeCredential(1), std::nullopt) ==
              ConfirmResult::kInvalid);
    }
    REQUIRE(session.TryConfirmCode("111111", now, "client-1", MakeCredential(1), std::nullopt) ==
            ConfirmResult::kRateLimited);

    auto later = now + std::chrono::seconds(61);
    CHECK(session.TryConfirmCode("111111", later, "client-1", MakeCredential(1), std::nullopt) ==
          ConfirmResult::kConfirmed);
}

TEST_CASE("TryStartChallenge issues a fresh code once the previous one expired",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"), std::chrono::seconds(0));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kExpired);

    auto retried = session.TryStartChallenge();

    REQUIRE(retried.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(retried.code.has_value());
    CHECK(*retried.code == "111111");
}

TEST_CASE("TryStartChallenge reports kGeneratorFailed distinctly from kAlreadyInProgress, and can "
          "retry after the failure",
          "[security][pairing_session]") {
    PairingSession session(FlakyCodeGenerator("111111"));

    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kGeneratorFailed);

    auto retried = session.TryStartChallenge();
    REQUIRE(retried.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(retried.code.has_value());
    CHECK(*retried.code == "111111");
}

TEST_CASE("PeekPending fails with no pending credential", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(session.PeekPending("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("PeekPending fails and leaves the pending credential intact for the wrong clientId",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.PeekPending("client-2", MakeCredential(1)).has_value());
    // The correct clientId+credential still peeks afterward.
    CHECK(session.PeekPending("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("PeekPending fails and leaves the pending credential intact for the wrong credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.PeekPending("client-1", MakeCredential(9)).has_value());
    CHECK(session.PeekPending("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("PeekPending does not consume the pending credential", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    auto first = session.PeekPending("client-1", MakeCredential(1));
    REQUIRE(first.has_value());
    CHECK_FALSE(first->displayName.has_value());

    // A second peek still finds it, with identical contents -- peeking alone must be safe to
    // repeat (for example across a failed TrustStore::Persist and its retry) and must not mutate
    // what it returns.
    auto second = session.PeekPending("client-1", MakeCredential(1));
    REQUIRE(second.has_value());
    CHECK(second->clientId == first->clientId);
    CHECK(second->credential == first->credential);
    CHECK(second->displayName == first->displayName);
}

TEST_CASE("a failed persist after PeekPending leaves the pending credential in place for a retry",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    // Simulates the caller peeking, then TrustStore::Persist failing -- CommitPending is
    // deliberately never called on this attempt.
    REQUIRE(session.PeekPending("client-1", MakeCredential(1)).has_value());

    // Still PENDING_CREDENTIAL: a new challenge must not be allowed to start over it.
    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kAlreadyInProgress);

    // The retry: peek again (still there), and this time persistence is simulated as succeeding.
    REQUIRE(session.PeekPending("client-1", MakeCredential(1)).has_value());
    CHECK(session.CommitPending("client-1", MakeCredential(1)));
    CHECK(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
}

TEST_CASE("CommitPending fails with no pending credential", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(1)));
}

TEST_CASE("CommitPending fails and leaves the pending credential intact for the wrong clientId",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.CommitPending("client-2", MakeCredential(1)));
    // The correct clientId+credential still commits afterward.
    CHECK(session.CommitPending("client-1", MakeCredential(1)));
}

TEST_CASE("CommitPending fails and leaves the pending credential intact for the wrong credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(9)));
    CHECK(session.CommitPending("client-1", MakeCredential(1)));
}

TEST_CASE("CommitPending consumes the pending credential exactly once", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().outcome == StartChallengeOutcome::kStarted);
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);
    REQUIRE(session.CommitPending("client-1", MakeCredential(1)));

    CHECK_FALSE(session.CommitPending("client-1", MakeCredential(1)));
    // Nothing left to peek either, once committed.
    CHECK_FALSE(session.PeekPending("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("DefaultCodeGenerator produces a six-digit numeric candidate", "[security][pairing_session]") {
    auto code = PairingSession::DefaultCodeGenerator();

    REQUIRE(code.has_value());
    CHECK(code->size() == 6);
    for (char ch : *code) {
        CHECK(std::isdigit(static_cast<unsigned char>(ch)));
    }
}
