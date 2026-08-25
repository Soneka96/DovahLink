#include "security/factory_reset_challenge.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cctype>
#include <chrono>
#include <deque>
#include <optional>
#include <string>

using dovahlink::security::FactoryResetChallenge;
using dovahlink::security::FactoryResetConfirmOutcome;

namespace {

///  Deterministic `FactoryResetChallenge::CodeGenerator` that always returns
///  `code`.
FactoryResetChallenge::CodeGenerator FixedCode(std::string code) {
    return
        [code = std::move(code)]() -> std::optional<std::string> { return code; };
}

///  `FactoryResetChallenge::CodeGenerator` that always fails, simulating a
///  CSPRNG failure.
FactoryResetChallenge::CodeGenerator FailingCodeGenerator() {
    return []() -> std::optional<std::string> { return std::nullopt; };
}

///  `FactoryResetChallenge::CodeGenerator` that fails once (simulating a
///  transient CSPRNG failure), then returns `code` on every later call.
FactoryResetChallenge::CodeGenerator FlakyCodeGenerator(std::string code) {
    auto failedOnce = std::make_shared<bool>(false);
    return [code = std::move(code), failedOnce]() -> std::optional<std::string> {
        if (!*failedOnce) {
            *failedOnce = true;
            return std::nullopt;
        }
        return code;
    };
}

///  Deterministic `FactoryResetChallenge::CodeGenerator` that returns each
///  queued code in order.
FactoryResetChallenge::CodeGenerator
SequentialCodes(std::deque<std::string> codes) {
    auto queue = std::make_shared<std::deque<std::string>>(std::move(codes));
    return [queue]() -> std::optional<std::string> {
        REQUIRE_FALSE(queue->empty());
        auto next = queue->front();
        queue->pop_front();
        return next;
    };
}

} //  namespace

TEST_CASE("TryStart returns the generated six-digit code",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"));

    auto code = challenge.TryStart();

    REQUIRE(code.has_value());
    CHECK(*code == "123456");
}

TEST_CASE("CodeTimeToLive reports the configured challenge lifetime",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge defaultChallenge(FixedCode("123456"));
    FactoryResetChallenge customChallenge(FixedCode("123456"),
                                          std::chrono::seconds(90));

    CHECK(defaultChallenge.CodeTimeToLive() == std::chrono::seconds(60));
    CHECK(customChallenge.CodeTimeToLive() == std::chrono::seconds(90));
}

TEST_CASE("TryStart propagates a code-generator failure",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FailingCodeGenerator());

    CHECK_FALSE(challenge.TryStart().has_value());
}

TEST_CASE(
    "TryStart succeeds on a later call after an earlier generator failure",
    "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FlakyCodeGenerator("123456"));
    REQUIRE_FALSE(challenge.TryStart().has_value());

    auto retried = challenge.TryStart();

    REQUIRE(retried.has_value());
    CHECK(*retried == "123456");
    CHECK(challenge.TryConfirm("123456") ==
          FactoryResetConfirmOutcome::kConfirmed);
}

TEST_CASE(
    "TryConfirm with the correct code confirms and consumes the challenge",
    "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"));
    REQUIRE(challenge.TryStart().has_value());

    CHECK(challenge.TryConfirm("123456") ==
          FactoryResetConfirmOutcome::kConfirmed);

    //  Consumed: the same correct code no longer confirms anything.
    CHECK(challenge.TryConfirm("123456") == FactoryResetConfirmOutcome::kExpired);
}

TEST_CASE("TryConfirm with a wrong code destroys the challenge immediately",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"));
    REQUIRE(challenge.TryStart().has_value());

    CHECK(challenge.TryConfirm("000000") == FactoryResetConfirmOutcome::kInvalid);

    //  No multi-attempt budget: the correct code no longer works either, since the
    //  one wrong attempt already destroyed the challenge.
    CHECK(challenge.TryConfirm("123456") == FactoryResetConfirmOutcome::kExpired);
}

TEST_CASE("TryConfirm with a wrong-length or empty presented code is treated "
          "as a wrong code",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"));
    REQUIRE(challenge.TryStart().has_value());

    CHECK(challenge.TryConfirm("1234567") ==
          FactoryResetConfirmOutcome::kInvalid);
}

TEST_CASE("TryConfirm with nothing ever started reports kExpired",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"));

    CHECK(challenge.TryConfirm("123456") == FactoryResetConfirmOutcome::kExpired);
}

TEST_CASE(
    "TryConfirm reports kExpired once the challenge's own TTL has elapsed",
    "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(FixedCode("123456"), std::chrono::seconds(0));
    REQUIRE(challenge.TryStart().has_value());

    CHECK(challenge.TryConfirm("123456") == FactoryResetConfirmOutcome::kExpired);
}

TEST_CASE("TryStart discards any previous challenge unconditionally",
          "[security][factory_reset_challenge]") {
    FactoryResetChallenge challenge(SequentialCodes({"111111", "222222"}));
    auto first = challenge.TryStart();
    REQUIRE(first.has_value());
    CHECK(*first == "111111");

    //  A fresh TryStart always succeeds and generates a new code -- there is no
    //  ownership model to resume, unlike PairingSession's kResumed outcome.
    auto second = challenge.TryStart();
    REQUIRE(second.has_value());
    CHECK(*second == "222222");

    //  The discarded first code no longer confirms anything against the fresh
    //  challenge.
    CHECK(challenge.TryConfirm("111111") == FactoryResetConfirmOutcome::kInvalid);
}

TEST_CASE("DefaultCodeGenerator produces a six-digit numeric candidate",
          "[security][factory_reset_challenge]") {
    auto code = FactoryResetChallenge::DefaultCodeGenerator();

    REQUIRE(code.has_value());
    CHECK(code->size() == 6);
    for (char ch : *code) {
        CHECK(std::isdigit(static_cast<unsigned char>(ch)));
    }
}
