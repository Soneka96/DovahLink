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

TEST_CASE("full pairing lifecycle: start, confirm, finalize returns to NONE",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("123456"));

    auto code = session.TryStartChallenge();
    REQUIRE(code.has_value());
    CHECK(*code == "123456");

    REQUIRE(session.TryConfirmCode("123456", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::string("My PC")) == ConfirmResult::kConfirmed);

    auto finalized = session.TryFinalize("client-1", MakeCredential(1));
    REQUIRE(finalized.has_value());
    CHECK(finalized->clientId == "client-1");
    CHECK(finalized->credential == MakeCredential(1));
    REQUIRE(finalized->displayName.has_value());
    CHECK(*finalized->displayName == "My PC");

    // Back to NONE: a fresh challenge can start again.
    CHECK(session.TryStartChallenge().has_value());
}

TEST_CASE("a second TryStartChallenge while CHALLENGE_ACTIVE does not replace the code",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());

    CHECK_FALSE(session.TryStartChallenge().has_value());

    // The original code still works -- it was not replaced or cleared.
    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kConfirmed);
}

TEST_CASE("TryStartChallenge fails while a credential is pending finalization",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.TryStartChallenge().has_value());
}

TEST_CASE("TryConfirmCode reports kInvalid with no active challenge", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kInvalid);
}

TEST_CASE("a wrong code does not consume the real one, which still succeeds afterward",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());

    CHECK(session.TryConfirmCode("000000", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kInvalid);
    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kConfirmed);
}

TEST_CASE("a code cannot be confirmed twice", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());
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
    REQUIRE(session.TryStartChallenge().has_value());

    CHECK(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                  std::nullopt) == ConfirmResult::kExpired);
}

TEST_CASE("repeated wrong codes block further attempts with kRateLimited", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());

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
    REQUIRE(session.TryStartChallenge().has_value());

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
    REQUIRE(session.TryStartChallenge().has_value());
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kExpired);

    auto retriedCode = session.TryStartChallenge();

    REQUIRE(retriedCode.has_value());
    CHECK(*retriedCode == "111111");
}

TEST_CASE("TryStartChallenge stays at NONE and can retry after a code-generator failure",
          "[security][pairing_session]") {
    PairingSession session(FlakyCodeGenerator("111111"));

    CHECK_FALSE(session.TryStartChallenge().has_value());

    auto retriedCode = session.TryStartChallenge();
    REQUIRE(retriedCode.has_value());
    CHECK(*retriedCode == "111111");
}

TEST_CASE("TryFinalize fails with no pending credential", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));

    CHECK_FALSE(session.TryFinalize("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("TryFinalize fails and leaves the pending credential intact for the wrong clientId",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.TryFinalize("client-2", MakeCredential(1)).has_value());
    // The correct clientId+credential still finalizes afterward.
    CHECK(session.TryFinalize("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("TryFinalize fails and leaves the pending credential intact for the wrong credential",
          "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);

    CHECK_FALSE(session.TryFinalize("client-1", MakeCredential(9)).has_value());
    CHECK(session.TryFinalize("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("TryFinalize consumes the pending credential exactly once", "[security][pairing_session]") {
    PairingSession session(FixedCode("111111"));
    REQUIRE(session.TryStartChallenge().has_value());
    REQUIRE(session.TryConfirmCode("111111", std::chrono::steady_clock::now(), "client-1", MakeCredential(1),
                                    std::nullopt) == ConfirmResult::kConfirmed);
    REQUIRE(session.TryFinalize("client-1", MakeCredential(1)).has_value());

    CHECK_FALSE(session.TryFinalize("client-1", MakeCredential(1)).has_value());
}

TEST_CASE("DefaultCodeGenerator produces a six-digit numeric candidate", "[security][pairing_session]") {
    auto code = PairingSession::DefaultCodeGenerator();

    REQUIRE(code.has_value());
    CHECK(code->size() == 6);
    for (char ch : *code) {
        CHECK(std::isdigit(static_cast<unsigned char>(ch)));
    }
}
