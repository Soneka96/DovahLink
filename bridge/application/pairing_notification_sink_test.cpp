#include "application/pairing_notification_sink.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>

using dovahlink::application::PairingNotificationSink;

namespace {

/// Captures the most recently notified pairing code, for tests and the Skyrim-independent test
/// harness to observe what the real Skyrim implementation would have displayed.
class RecordingPairingNotificationSink : public PairingNotificationSink {
public:
    /// @copydoc PairingNotificationSink::NotifyPairingCodeAvailable
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        lastNotifiedCode_ = std::string(sixDigitCode);
        notificationCount_ += 1;
    }

    /// The most recent code passed to `NotifyPairingCodeAvailable`, if any.
    [[nodiscard]] const std::optional<std::string>& LastNotifiedCode() const { return lastNotifiedCode_; }

    /// Number of times `NotifyPairingCodeAvailable` was called.
    [[nodiscard]] int NotificationCount() const { return notificationCount_; }

private:
    /// Most recent code observed, or no value before the first call.
    std::optional<std::string> lastNotifiedCode_;

    /// Total calls observed.
    int notificationCount_ = 0;
};

}  // namespace

TEST_CASE("RecordingPairingNotificationSink captures the notified code", "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingCodeAvailable("123456");

    REQUIRE(sink.LastNotifiedCode().has_value());
    CHECK(*sink.LastNotifiedCode() == "123456");
    CHECK(sink.NotificationCount() == 1);
}

TEST_CASE("RecordingPairingNotificationSink reflects only the most recent code across multiple calls",
          "[application][pairing_notification_sink]") {
    RecordingPairingNotificationSink sink;

    sink.NotifyPairingCodeAvailable("111111");
    sink.NotifyPairingCodeAvailable("222222");

    REQUIRE(sink.LastNotifiedCode().has_value());
    CHECK(*sink.LastNotifiedCode() == "222222");
    CHECK(sink.NotificationCount() == 2);
}
