#include "application/pairing_handler.hpp"

#include "protocol/messages.hpp"
#include "security/hex.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>

#include <chrono>
#include <optional>
#include <string>
#include <vector>

using dovahlink::application::ConnectionId;
using dovahlink::application::HandlePairingAck;
using dovahlink::application::HandlePairingCancel;
using dovahlink::application::HandlePairingConfirm;
using dovahlink::application::HandlePairingRenotify;
using dovahlink::application::HandlePairingRequest;
using dovahlink::application::PairingNotificationSink;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;
using dovahlink::protocol::Envelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::EncodeHex;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::PairingSession;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;

namespace {

constexpr ConnectionId kConnection = 1;
constexpr const char* kSessionId = "session-1";
constexpr const char* kClientId = "client-1";
constexpr const char* kOtherClientId = "client-2";

/// `ITrustStorePersistence` double that always loads an empty snapshot.
class EmptyPersistence : public ITrustStorePersistence {
public:
    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

/// `ITrustStorePersistence` double that always loads an empty snapshot but fails every `Save`,
/// for exercising `TrustStore::Persist` failure.
class FailingSavePersistence : public ITrustStorePersistence {
public:
    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }
    bool Save(const TrustStoreSnapshot&) override { return false; }
};

/// `ITrustStorePersistence` double that fails exactly one `Save`, then succeeds on every call
/// after -- simulates a transient save failure a retried `pairing_ack` should recover from.
class FlakyOnceSavePersistence : public ITrustStorePersistence {
public:
    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }
    bool Save(const TrustStoreSnapshot&) override {
        if (!failedOnce_) {
            failedOnce_ = true;
            return false;
        }
        return true;
    }

private:
    bool failedOnce_ = false;
};

/// Captures every distinct notification `PairingNotificationSink` call this handler makes.
class RecordingPairingNotificationSink : public PairingNotificationSink {
public:
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        codes.emplace_back(sixDigitCode);
    }
    void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) override {
        incorrectCodes.emplace_back(sixDigitCode);
    }
    void NotifyPairingAttemptsExhausted() override { ++attemptsExhaustedCount; }

    /// Every code observed via `NotifyPairingCodeAvailable`, in call order.
    std::vector<std::string> codes;

    /// Every code observed via `NotifyPairingCodeIncorrect`, in call order.
    std::vector<std::string> incorrectCodes;

    /// Number of times `NotifyPairingAttemptsExhausted` was called.
    int attemptsExhaustedCount = 0;
};

/// Builds a `pairing_request` envelope (no payload).
Envelope BuildPairingRequestEnvelope(std::string messageId = "message-request-1") {
    return Envelope{
        .messageType = "pairing_request",
        .messageId = std::move(messageId),
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
}

/// Builds a `pairing_confirm` envelope with the given code and optional display name.
Envelope BuildPairingConfirmEnvelope(const std::string& code, std::optional<std::string> displayName = std::nullopt,
                                      std::string messageId = "message-confirm-1") {
    boost::json::object payload;
    payload["code"] = code;
    payload["displayName"] = displayName.has_value() ? boost::json::value(*displayName) : boost::json::value(nullptr);
    return Envelope{
        .messageType = "pairing_confirm",
        .messageId = std::move(messageId),
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

/// Builds a `pairing_renotify` envelope (no payload).
Envelope BuildPairingRenotifyEnvelope(std::string messageId = "message-renotify-1") {
    return Envelope{
        .messageType = "pairing_renotify",
        .messageId = std::move(messageId),
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
}

/// Builds a `pairing_cancel` envelope (no payload).
Envelope BuildPairingCancelEnvelope(std::string messageId = "message-cancel-1") {
    return Envelope{
        .messageType = "pairing_cancel",
        .messageId = std::move(messageId),
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
}

/// Builds a `pairing_ack` envelope echoing the given hex-encoded credential.
Envelope BuildPairingAckEnvelope(const std::string& hexCredential, std::string messageId = "message-ack-1") {
    boost::json::object payload;
    payload["credential"] = hexCredential;
    return Envelope{
        .messageType = "pairing_ack",
        .messageId = std::move(messageId),
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

}  // namespace

TEST_CASE("full pairing flow: request, confirm, ack results in a trusted, upgraded session",
          "[application][pairing_handler]") {
    PairingSession pairingSession(
        []() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    auto statusResponse = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession,
                                                sink, now);
    REQUIRE(statusResponse.messageType == "pairing_status");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(statusResponse.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "available");
    REQUIRE(status->expiresInSeconds.has_value());
    CHECK(*status->expiresInSeconds > 0);
    REQUIRE(sink.codes.size() == 1);
    CHECK(sink.codes[0] == "123456");

    auto confirmResponse = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456", std::string("My PC")),
                                                 kSessionId, kClientId, pairingSession, sink, now);
    REQUIRE(confirmResponse.messageType == "pairing_outcome");
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    CHECK(confirmOutcome->outcome == "credential_issued");
    REQUIRE(confirmOutcome->credential.has_value());
    CHECK_FALSE(confirmOutcome->shortId.has_value());
    REQUIRE(confirmOutcome->displayName.has_value());
    CHECK(*confirmOutcome->displayName == "My PC");
    std::string credentialHex = *confirmOutcome->credential;

    auto ackResponse = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                         pairingSession, trustStore, sessions, now);
    REQUIRE(ackResponse.messageType == "pairing_outcome");
    auto ackOutcome = dovahlink::protocol::DecodePairingOutcomePayload(ackResponse.payload);
    REQUIRE(ackOutcome.has_value());
    CHECK(ackOutcome->outcome == "trusted");
    REQUIRE(ackOutcome->credential.has_value());
    CHECK(*ackOutcome->credential == credentialHex);
    REQUIRE(ackOutcome->shortId.has_value());
    REQUIRE(ackOutcome->displayName.has_value());
    CHECK(*ackOutcome->displayName == "My PC");

    CHECK(sessions.IsFullyTrusted(kConnection));
    auto record = trustStore.Query(kClientId);
    REQUIRE(record.has_value());
    CHECK(EncodeHex(record->credential) == credentialHex);
}

TEST_CASE("a second pairing_request from the same client reports in_progress with a live "
          "countdown and issues no second notification",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    auto first = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink, now);
    auto firstStatus = dovahlink::protocol::DecodePairingStatusPayload(first.payload);
    REQUIRE(firstStatus.has_value());
    CHECK(firstStatus->state == "available");

    auto second = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                        pairingSession, sink, now);
    auto secondStatus = dovahlink::protocol::DecodePairingStatusPayload(second.payload);
    REQUIRE(secondStatus.has_value());
    CHECK(secondStatus->state == "in_progress");
    REQUIRE(secondStatus->expiresInSeconds.has_value());
    CHECK(*secondStatus->expiresInSeconds > 0);

    // Exactly one notification for the whole exchange.
    CHECK(sink.codes.size() == 1);
}

TEST_CASE("a pairing_request from the same client reports in_progress with null expiresInSeconds "
          "once its own code is already confirmed and pending acknowledgement",
          "[application][pairing_handler]") {
    // Pins protocol/schema/README.md's pairing_status contract: expiresInSeconds tracks the
    // *code's* own remaining validity, not "how long until this state changes" in general -- once
    // the owner's code is consumed by a successful pairing_confirm (PENDING_CREDENTIAL), a repeated
    // pairing_request still resumes as "in_progress" (the client still owns something to resume),
    // but there is no code left to count down, so expiresInSeconds is null (present, not omitted --
    // omission is reserved for other_device_pairing).
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    auto requested = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink, now);
    REQUIRE(dovahlink::protocol::DecodePairingStatusPayload(requested.payload)->state == "available");

    auto confirmed =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmedOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmed.payload);
    REQUIRE(confirmedOutcome.has_value());
    REQUIRE(confirmedOutcome->outcome == "credential_issued");

    auto resumed = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                         pairingSession, sink, now);
    auto resumedStatus = dovahlink::protocol::DecodePairingStatusPayload(resumed.payload);
    REQUIRE(resumedStatus.has_value());
    CHECK(resumedStatus->state == "in_progress");
    CHECK_FALSE(resumedStatus->expiresInSeconds.has_value());
}

TEST_CASE("a pairing_request from a different client reports other_device_pairing and reveals no "
          "expiresInSeconds",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    auto owner = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink, now);
    REQUIRE(dovahlink::protocol::DecodePairingStatusPayload(owner.payload)->state == "available");

    auto competing = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId,
                                           kOtherClientId, pairingSession, sink, now);
    auto competingStatus = dovahlink::protocol::DecodePairingStatusPayload(competing.payload);
    REQUIRE(competingStatus.has_value());
    CHECK(competingStatus->state == "other_device_pairing");
    CHECK_FALSE(competingStatus->expiresInSeconds.has_value());

    // Still exactly one notification -- the competing client never triggers one.
    CHECK(sink.codes.size() == 1);
}

TEST_CASE("HandlePairingRequest reports unavailable when the code generator fails, distinct from "
          "in_progress",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::nullopt; });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    auto response = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                          now);

    CHECK(response.messageType == "pairing_status");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(response.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "unavailable");
    CHECK_FALSE(status->expiresInSeconds.has_value());
    // Nothing was generated to display -- the sink must never be notified with no code.
    CHECK(sink.codes.empty());
}

TEST_CASE("HandlePairingConfirm reports expired for an expired code", "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); },
                                   std::chrono::seconds(0));
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "expired");
    CHECK_FALSE(outcome->credential.has_value());
    // Expiry is not a wrong-code attempt -- never redisplayed.
    CHECK(sink.incorrectCodes.empty());
}

TEST_CASE("HandlePairingConfirm reports invalid for a wrong code and redisplays it as incorrect",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId, pairingSession, sink, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "invalid");
    REQUIRE(sink.incorrectCodes.size() == 1);
    CHECK(sink.incorrectCodes[0] == "123456");
    CHECK(sink.attemptsExhaustedCount == 0);
}

TEST_CASE("HandlePairingConfirm reports pacing_limited for an attempt evaluated too soon after the "
          "previous one, without redisplaying the code",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    static_cast<void>(
        HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId, pairingSession, sink, now));

    auto response =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId, pairingSession, sink, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pacing_limited");
    REQUIRE(outcome->retryAfterSeconds.has_value());
    CHECK(*outcome->retryAfterSeconds == 1);
    // The paced-out attempt is never evaluated, so it is never redisplayed either.
    CHECK(sink.incorrectCodes.size() == 1);
}

TEST_CASE("HandlePairingConfirm reports hard_limit_reached on the fifth wrong evaluated attempt "
          "and displays a distinct exhausted notification instead of redisplaying the code",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    for (int i = 0; i < 4; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        static_cast<void>(HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId,
                                                pairingSession, sink, attemptTime));
    }
    auto response = HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId, pairingSession,
                                          sink, now + std::chrono::seconds(4));

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "hard_limit_reached");
    CHECK(sink.attemptsExhaustedCount == 1);
    // Only the first wrong attempt redisplays -- the next three land inside its 5-second
    // auto-renotify cooldown, and the fifth (the hard-limit attempt itself) uses the distinct
    // exhausted notification instead of the ordinary incorrect-code path.
    CHECK(sink.incorrectCodes.size() == 1);
}

TEST_CASE("a non-owning client's wrong-code confirm reports invalid and triggers no notification",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    // kOtherClientId does not own the active challenge -- even the genuinely correct code is
    // rejected as though it were wrong, and no notification of any kind fires for it.
    auto response = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kOtherClientId,
                                          pairingSession, sink, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "invalid");
    CHECK(sink.incorrectCodes.empty());

    // The real owner's correct code still works afterward -- the non-owner's attempt did not
    // consume or invalidate it.
    auto ownerResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto ownerOutcome = dovahlink::protocol::DecodePairingOutcomePayload(ownerResponse.payload);
    REQUIRE(ownerOutcome.has_value());
    CHECK(ownerOutcome->outcome == "credential_issued");
}

TEST_CASE("a fresh pairing_request succeeds immediately after hard_limit_reached cancels the "
          "previous challenge",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    for (int i = 0; i < 5; ++i) {
        auto attemptTime = now + std::chrono::seconds(i);
        static_cast<void>(HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId,
                                                pairingSession, sink, attemptTime));
    }

    auto retryTime = now + std::chrono::seconds(5);
    auto response = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                          pairingSession, sink, retryTime);

    auto status = dovahlink::protocol::DecodePairingStatusPayload(response.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "available");
    REQUIRE(sink.codes.size() == 2);
    CHECK(sink.codes[1] == "123456");
}

TEST_CASE("HandlePairingAck reports pending_not_found once the pending credential's 5-minute TTL "
          "has elapsed",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto pastTtl = now + std::chrono::minutes(5);
    auto response = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions, pastTtl);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pending_not_found");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));
}

TEST_CASE("HandlePairingConfirm is rejected as malformed when the code field is missing",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;

    Envelope confirmEnvelope{
        .messageType = "pairing_confirm",
        .messageId = "message-confirm-1",
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
    auto response = HandlePairingConfirm(confirmEnvelope, kSessionId, kClientId, pairingSession, sink,
                                          std::chrono::steady_clock::now());

    CHECK(response.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandlePairingAck reports pending_not_found for a mismatched credential",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    static_cast<void>(
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now));

    auto response =
        HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9})), kSessionId,
                          kClientId, kConnection, pairingSession, trustStore, sessions, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pending_not_found");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));
}

TEST_CASE("HandlePairingAck reports pending_not_found with no pending credential (simulating a "
          "Bridge restart)",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());

    auto response =
        HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{1, 2, 3, 4})), kSessionId,
                          kClientId, kConnection, pairingSession, trustStore, sessions,
                          std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pending_not_found");
}

TEST_CASE("HandlePairingAck reports already_trusted on a retry after the credential is already committed",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto firstAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions, now);
    auto firstOutcome = dovahlink::protocol::DecodePairingOutcomePayload(firstAck.payload);
    REQUIRE(firstOutcome.has_value());
    REQUIRE(firstOutcome->outcome == "trusted");
    REQUIRE(firstOutcome->shortId.has_value());

    // Simulates a lost success response: the client resends pairing_ack with the same credential.
    auto retryAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions, now);
    auto retryOutcome = dovahlink::protocol::DecodePairingOutcomePayload(retryAck.payload);
    REQUIRE(retryOutcome.has_value());
    CHECK(retryOutcome->outcome == "already_trusted");
    CHECK(sessions.IsFullyTrusted(kConnection));

    // The retry describes the SAME record, not a different or stale one.
    CHECK(retryOutcome->credential == firstOutcome->credential);
    CHECK(retryOutcome->shortId == firstOutcome->shortId);
    CHECK(retryOutcome->displayName == firstOutcome->displayName);
}

TEST_CASE("HandlePairingAck reports internal_error when TrustStore::Persist fails to save",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    FailingSavePersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto ackResponse = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                         pairingSession, trustStore, sessions, now);

    CHECK(ackResponse.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(ackResponse.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "internal_error");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));
}

TEST_CASE("HandlePairingAck recovers on retry after a transient persistence failure",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    FlakyOnceSavePersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    // First attempt: the underlying save fails transiently.
    auto firstAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions, now);
    CHECK(firstAck.messageType == "error");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));

    // Retry with the same acknowledgement: the pending credential is still there, and this time
    // the save succeeds.
    auto retryAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex, "message-ack-2"), kSessionId, kClientId,
                                      kConnection, pairingSession, trustStore, sessions, now);
    auto retryOutcome = dovahlink::protocol::DecodePairingOutcomePayload(retryAck.payload);
    REQUIRE(retryOutcome.has_value());
    CHECK(retryOutcome->outcome == "trusted");
    CHECK(sessions.IsFullyTrusted(kConnection));
    auto record = trustStore.Query(kClientId);
    REQUIRE(record.has_value());
    CHECK(EncodeHex(record->credential) == credentialHex);
}

TEST_CASE("two separate successful pairing flows issue different credentials",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto firstConfirm =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto firstOutcome = dovahlink::protocol::DecodePairingOutcomePayload(firstConfirm.payload);
    REQUIRE(firstOutcome.has_value());
    REQUIRE(firstOutcome->credential.has_value());
    // Finalize to return PairingSession to NONE so a second challenge can start.
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    static_cast<void>(HandlePairingAck(BuildPairingAckEnvelope(*firstOutcome->credential), kSessionId, kClientId,
                                        kConnection, pairingSession, trustStore, sessions, now));

    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                            pairingSession, sink, now));
    auto secondConfirm = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456", std::nullopt, "message-confirm-2"),
                                               kSessionId, kClientId, pairingSession, sink, now);
    auto secondOutcome = dovahlink::protocol::DecodePairingOutcomePayload(secondConfirm.payload);
    REQUIRE(secondOutcome.has_value());
    REQUIRE(secondOutcome->credential.has_value());

    CHECK(*firstOutcome->credential != *secondOutcome->credential);
}

TEST_CASE("HandlePairingAck does not upgrade to full trust when the presented clientId collides "
          "with an unrelated trusted client but the credential does not match",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore.Persist(kClientId, std::vector<std::uint8_t>{1, 2, 3, 4}, std::nullopt).has_value());
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());

    // No pairing_confirm ever ran on this session -- an unpaired connection simply claiming the
    // same clientId and guessing at a credential.
    auto response =
        HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9})), kSessionId,
                          kClientId, kConnection, pairingSession, trustStore, sessions,
                          std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pending_not_found");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));
}

TEST_CASE("HandlePairingAck is rejected as malformed when the credential field is missing",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());

    Envelope ackEnvelope{
        .messageType = "pairing_ack",
        .messageId = "message-ack-1",
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
    auto response = HandlePairingAck(ackEnvelope, kSessionId, kClientId, kConnection, pairingSession, trustStore,
                                      sessions, std::chrono::steady_clock::now());

    CHECK(response.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandlePairingRenotify redisplays the owner's code and reports renotified",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                           now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "renotified");
    // The original display plus this redisplay -- same code both times.
    REQUIRE(sink.codes.size() == 2);
    CHECK(sink.codes[1] == "123456");
}

TEST_CASE("HandlePairingRenotify reports renotify_cooldown with no redisplay while its own cooldown "
          "is active",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    static_cast<void>(
        HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kClientId, pairingSession, sink, now));

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope("message-renotify-2"), kSessionId, kClientId,
                                           pairingSession, sink, now + std::chrono::seconds(2));

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "renotify_cooldown");
    REQUIRE(outcome->retryAfterSeconds.has_value());
    CHECK(*outcome->retryAfterSeconds == 3);
    // Only the first renotify actually redisplayed.
    REQUIRE(sink.codes.size() == 2);
}

TEST_CASE("HandlePairingRenotify reports already_idle for a client owning no challenge",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                           std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_idle");
    CHECK(sink.codes.empty());
}

TEST_CASE("HandlePairingRenotify succeeds again once its cooldown elapses",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    static_cast<void>(
        HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kClientId, pairingSession, sink, now));

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope("message-renotify-2"), kSessionId, kClientId,
                                           pairingSession, sink, now + std::chrono::seconds(5));

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "renotified");
    // The original display, plus one redisplay per successful renotify -- both the first (at
    // `now`) and this second one (at `now + 5s`, exactly when its cooldown elapses) succeeded.
    REQUIRE(sink.codes.size() == 3);
    CHECK(sink.codes[1] == "123456");
    CHECK(sink.codes[2] == "123456");
}

TEST_CASE("HandlePairingRenotify reports already_idle once the challenge reaches PENDING_CREDENTIAL",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    static_cast<void>(
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now));

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                           now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_idle");
    // Only the original display -- a pending credential has no code left to redisplay.
    CHECK(sink.codes.size() == 1);
}

TEST_CASE("HandlePairingRenotify reports already_idle for a non-owner without disturbing the "
          "real owner's challenge",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response = HandlePairingRenotify(BuildPairingRenotifyEnvelope(), kSessionId, kOtherClientId, pairingSession,
                                           sink, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_idle");
    // Only the original display -- the non-owner's request never redisplayed anything.
    CHECK(sink.codes.size() == 1);

    auto ownerResponse =
        HandlePairingRenotify(BuildPairingRenotifyEnvelope("message-renotify-2"), kSessionId, kClientId,
                               pairingSession, sink, now);
    auto ownerOutcome = dovahlink::protocol::DecodePairingOutcomePayload(ownerResponse.payload);
    REQUIRE(ownerOutcome.has_value());
    CHECK(ownerOutcome->outcome == "renotified");
}

TEST_CASE("HandlePairingCancel clears an owned active challenge and frees it for a fresh "
          "pairing_request",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response = HandlePairingCancel(BuildPairingCancelEnvelope(), kSessionId, kClientId, pairingSession, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "cancelled");

    auto fresh = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                       pairingSession, sink, now);
    auto freshStatus = dovahlink::protocol::DecodePairingStatusPayload(fresh.payload);
    REQUIRE(freshStatus.has_value());
    CHECK(freshStatus->state == "available");
}

TEST_CASE("HandlePairingCancel clears an owned pending credential, leaving it unreachable by a "
          "later pairing_ack",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, sink, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto response = HandlePairingCancel(BuildPairingCancelEnvelope(), kSessionId, kClientId, pairingSession, now);
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "cancelled");

    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    SessionManager sessions;
    auto sessionLease = sessions.TryCreateSession(kConnection, kSessionId, kClientId, SessionTrustTier::kRestricted);
    REQUIRE(sessionLease.has_value());
    auto ackResponse = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                         pairingSession, trustStore, sessions, now);
    auto ackOutcome = dovahlink::protocol::DecodePairingOutcomePayload(ackResponse.payload);
    REQUIRE(ackOutcome.has_value());
    CHECK(ackOutcome->outcome == "pending_not_found");

    // The slot is freed too, not just unreachable by the stale credential.
    auto fresh = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, kClientId,
                                       pairingSession, sink, now);
    auto freshStatus = dovahlink::protocol::DecodePairingStatusPayload(fresh.payload);
    REQUIRE(freshStatus.has_value());
    CHECK(freshStatus->state == "available");
}

TEST_CASE("HandlePairingCancel is idempotent: repeating it reports already_idle truthfully",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));
    auto first = HandlePairingCancel(BuildPairingCancelEnvelope(), kSessionId, kClientId, pairingSession, now);
    auto firstOutcome = dovahlink::protocol::DecodePairingOutcomePayload(first.payload);
    REQUIRE(firstOutcome.has_value());
    REQUIRE(firstOutcome->outcome == "cancelled");

    auto second =
        HandlePairingCancel(BuildPairingCancelEnvelope("message-cancel-2"), kSessionId, kClientId, pairingSession, now);

    auto secondOutcome = dovahlink::protocol::DecodePairingOutcomePayload(second.payload);
    REQUIRE(secondOutcome.has_value());
    CHECK(secondOutcome->outcome == "already_idle");
}

TEST_CASE("HandlePairingCancel reports already_idle with nothing owned",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });

    auto response = HandlePairingCancel(BuildPairingCancelEnvelope(), kSessionId, kClientId, pairingSession,
                                         std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_idle");
}

TEST_CASE("HandlePairingCancel reports already_idle for a non-owner without disturbing the real "
          "owner's challenge",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, kClientId, pairingSession, sink,
                                            now));

    auto response = HandlePairingCancel(BuildPairingCancelEnvelope(), kSessionId, kOtherClientId, pairingSession, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_idle");

    // The real owner's challenge survives the non-owner's cancel attempt untouched.
    auto ownerResponse = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId,
                                               pairingSession, sink, now);
    auto ownerOutcome = dovahlink::protocol::DecodePairingOutcomePayload(ownerResponse.payload);
    REQUIRE(ownerOutcome.has_value());
    CHECK(ownerOutcome->outcome == "credential_issued");
}
