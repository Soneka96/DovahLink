#include "application/replay_guard.hpp"

#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>

using dovahlink::application::MessageIdCheckResult;
using dovahlink::application::ReplayGuard;

TEST_CASE("Count is zero on a freshly constructed guard",
          "[application][replay_guard]") {
  ReplayGuard guard;
  CHECK(guard.Count() == 0);
}

TEST_CASE("RecordMessage accepts a new messageId",
          "[application][replay_guard]") {
  ReplayGuard guard;
  CHECK(guard.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
}

TEST_CASE("RecordMessage rejects a duplicate messageId as replayed",
          "[application][replay_guard]") {
  ReplayGuard guard;
  REQUIRE(guard.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
  CHECK(guard.RecordMessage("message-1") == MessageIdCheckResult::kReplayed);
}

TEST_CASE("a replayed message does not block a later distinct message",
          "[application][replay_guard]") {
  ReplayGuard guard;
  REQUIRE(guard.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
  REQUIRE(guard.RecordMessage("message-1") == MessageIdCheckResult::kReplayed);
  CHECK(guard.RecordMessage("message-2") == MessageIdCheckResult::kAccepted);
}

TEST_CASE("Count reflects only distinct accepted messageIds",
          "[application][replay_guard]") {
  ReplayGuard guard;
  (void)guard.RecordMessage("message-1");
  (void)guard.RecordMessage("message-1"); // replay, not counted again
  (void)guard.RecordMessage(
      "message-1"); // replayed repeatedly, still not counted again
  (void)guard.RecordMessage("message-2");
  CHECK(guard.Count() == 2);
}

TEST_CASE("two ReplayGuard instances track messageIds independently",
          "[application][replay_guard]") {
  ReplayGuard first;
  ReplayGuard second;
  REQUIRE(first.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
  // The same messageId on a different session's guard is not a replay of the
  // first.
  CHECK(second.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
  CHECK(first.Count() == 1);
  CHECK(second.Count() == 1);
}

TEST_CASE("RecordMessage remains a replay check beyond the separate session "
          "message limit",
          "[application][replay_guard]") {
  ReplayGuard guard;
  for (std::size_t i = 0; i <= dovahlink::security::kMaxMessagesPerSession;
       ++i) {
    REQUIRE(guard.RecordMessage("message-" + std::to_string(i)) ==
            MessageIdCheckResult::kAccepted);
  }
  CHECK(guard.Count() == dovahlink::security::kMaxMessagesPerSession + 1);
  CHECK(guard.RecordMessage("message-0") == MessageIdCheckResult::kReplayed);
}
