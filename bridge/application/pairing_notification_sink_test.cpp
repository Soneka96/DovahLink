#include "application/pairing_notification_sink.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>

using dovahlink::application::IPairingNotificationSink;

namespace {

///  Captures the most recently notified pairing code and every distinct
///  notification kind, for tests and the Skyrim-independent test harness to
///  observe what the real Skyrim implementation would have displayed.
class RecordingPairingNotificationSink : public IPairingNotificationSink {
  public:
    ///  @copydoc IPairingNotificationSink::NotifyPairingCodeAvailable
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        lastNotifiedCode_ = std::string(sixDigitCode);
        notificationCount_ += 1;
    }

    ///  @copydoc IPairingNotificationSink::NotifyPairingCodeIncorrect
    void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) override {
        lastIncorrectCode_ = std::string(sixDigitCode);
        incorrectCount_ += 1;
    }

    ///  @copydoc IPairingNotificationSink::NotifyPairingAttemptsExhausted
    void NotifyPairingAttemptsExhausted() override { exhaustedCount_ += 1; }

    ///  The most recent code passed to `NotifyPairingCodeAvailable`, if any.
    [[nodiscard]] const std::optional<std::string>& LastNotifiedCode() const {
        return lastNotifiedCode_;
    }

    ///  Number of times `NotifyPairingCodeAvailable` was called.
    [[nodiscard]] int NotificationCount() const { return notificationCount_; }

    ///  The most recent code passed to `NotifyPairingCodeIncorrect`, if any.
    [[nodiscard]] const std::optional<std::string>& LastIncorrectCode() const {
        return lastIncorrectCode_;
    }

    ///  Number of times `NotifyPairingCodeIncorrect` was called.
    [[nodiscard]] int IncorrectCount() const { return incorrectCount_; }

    ///  Number of times `NotifyPairingAttemptsExhausted` was called.
    [[nodiscard]] int ExhaustedCount() const { return exhaustedCount_; }

  private:
    ///  Most recent code observed via `NotifyPairingCodeAvailable`, or no value
    ///  before the first call.
    std::optional<std::string> lastNotifiedCode_;

    ///  Total `NotifyPairingCodeAvailable` calls observed.
    int notificationCount_ = 0;

    ///  Most recent code observed via `NotifyPairingCodeIncorrect`, or no value
    ///  before the first call.
    std::optional<std::string> lastIncorrectCode_;

    ///  Total `NotifyPairingCodeIncorrect` calls observed.
    int incorrectCount_ = 0;

    ///  Total `NotifyPairingAttemptsExhausted` calls observed.
    int exhaustedCount_ = 0;
};

} //  namespace

TEST_CASE("RecordingPairingNotificationSink captures the notified code",
          "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingCodeAvailable("123456");

    REQUIRE(sink.LastNotifiedCode().has_value());
    CHECK(*sink.LastNotifiedCode() == "123456");
    CHECK(sink.NotificationCount() == 1);
}

TEST_CASE("RecordingPairingNotificationSink reflects only the most recent code "
          "across multiple calls",
          "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingCodeAvailable("111111");
    sink.NotifyPairingCodeAvailable("222222");

    REQUIRE(sink.LastNotifiedCode().has_value());
    CHECK(*sink.LastNotifiedCode() == "222222");
    CHECK(sink.NotificationCount() == 2);
}

TEST_CASE("RecordingPairingNotificationSink captures a wrong-code redisplay "
          "independently of the "
          "original display",
          "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingCodeAvailable("123456");
    sink.NotifyPairingCodeIncorrect("123456");

    CHECK(sink.NotificationCount() == 1);
    REQUIRE(sink.LastIncorrectCode().has_value());
    CHECK(*sink.LastIncorrectCode() == "123456");
    CHECK(sink.IncorrectCount() == 1);
}

TEST_CASE("RecordingPairingNotificationSink captures an attempts-exhausted "
          "notification",
          "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingAttemptsExhausted();

    CHECK(sink.ExhaustedCount() == 1);
    CHECK(sink.NotificationCount() == 0);
    CHECK(sink.IncorrectCount() == 0);
}
