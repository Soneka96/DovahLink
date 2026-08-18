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
using dovahlink::application::HandlePairingConfirm;
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

/// Captures every pairing code this sink was asked to display.
class RecordingPairingNotificationSink : public PairingNotificationSink {
public:
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        codes.emplace_back(sixDigitCode);
    }

    /// Every code observed, in call order.
    std::vector<std::string> codes;
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

    auto statusResponse = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink);
    REQUIRE(statusResponse.messageType == "pairing_status");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(statusResponse.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "available");
    REQUIRE(sink.codes.size() == 1);
    CHECK(sink.codes[0] == "123456");

    auto confirmResponse = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456", std::string("My PC")),
                                                 kSessionId, kClientId, pairingSession, now);
    REQUIRE(confirmResponse.messageType == "pairing_outcome");
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    CHECK(confirmOutcome->outcome == "credential_issued");
    REQUIRE(confirmOutcome->credential.has_value());
    CHECK_FALSE(confirmOutcome->shortId.has_value());
    REQUIRE(confirmOutcome->displayName.has_value());
    CHECK(*confirmOutcome->displayName == "My PC");
    std::string credentialHex = *confirmOutcome->credential;

    auto ackResponse =
        HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection, pairingSession,
                          trustStore, sessions);
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

TEST_CASE("a second pairing_request while a challenge is active reports in_progress and issues no "
          "second notification",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;

    auto first = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink);
    auto firstStatus = dovahlink::protocol::DecodePairingStatusPayload(first.payload);
    REQUIRE(firstStatus.has_value());
    CHECK(firstStatus->state == "available");

    auto second = HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, pairingSession, sink);
    auto secondStatus = dovahlink::protocol::DecodePairingStatusPayload(second.payload);
    REQUIRE(secondStatus.has_value());
    CHECK(secondStatus->state == "in_progress");

    // Exactly one notification for the whole exchange.
    CHECK(sink.codes.size() == 1);
}

TEST_CASE("HandlePairingRequest reports unavailable when the code generator fails, distinct from "
          "in_progress",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::nullopt; });
    RecordingPairingNotificationSink sink;

    auto response = HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink);

    CHECK(response.messageType == "pairing_status");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(response.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "unavailable");
    // Nothing was generated to display -- the sink must never be notified with no code.
    CHECK(sink.codes.empty());
}

TEST_CASE("HandlePairingConfirm reports expired for an expired code", "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); },
                                   std::chrono::seconds(0));
    RecordingPairingNotificationSink sink;
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));

    auto response = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId,
                                          pairingSession, std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "expired");
    CHECK_FALSE(outcome->credential.has_value());
}

TEST_CASE("HandlePairingConfirm reports invalid for a wrong code", "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));

    auto response = HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId,
                                          pairingSession, std::chrono::steady_clock::now());

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "invalid");
}

TEST_CASE("HandlePairingConfirm reports rate_limited after repeated wrong codes",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));
    auto now = std::chrono::steady_clock::now();

    for (int i = 0; i < 5; ++i) {
        static_cast<void>(
            HandlePairingConfirm(BuildPairingConfirmEnvelope("000000"), kSessionId, kClientId, pairingSession, now));
    }
    auto response = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId,
                                          pairingSession, now);

    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "rate_limited");
}

TEST_CASE("HandlePairingConfirm is rejected as malformed when the code field is missing",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });

    Envelope confirmEnvelope{
        .messageType = "pairing_confirm",
        .messageId = "message-confirm-1",
        .sessionId = kSessionId,
        .correlationId = std::nullopt,
        .payload = boost::json::object{},
    };
    auto response =
        HandlePairingConfirm(confirmEnvelope, kSessionId, kClientId, pairingSession, std::chrono::steady_clock::now());

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
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));
    static_cast<void>(
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, now));

    auto response = HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9})),
                                      kSessionId, kClientId, kConnection, pairingSession, trustStore, sessions);

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

    auto response = HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{1, 2, 3, 4})),
                                      kSessionId, kClientId, kConnection, pairingSession, trustStore, sessions);

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
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto firstAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions);
    auto firstOutcome = dovahlink::protocol::DecodePairingOutcomePayload(firstAck.payload);
    REQUIRE(firstOutcome.has_value());
    REQUIRE(firstOutcome->outcome == "trusted");
    REQUIRE(firstOutcome->shortId.has_value());

    // Simulates a lost success response: the client resends pairing_ack with the same credential.
    auto retryAck = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                      pairingSession, trustStore, sessions);
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
    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));
    auto confirmResponse =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, now);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(confirmResponse.payload);
    REQUIRE(confirmOutcome.has_value());
    std::string credentialHex = *confirmOutcome->credential;

    auto ackResponse = HandlePairingAck(BuildPairingAckEnvelope(credentialHex), kSessionId, kClientId, kConnection,
                                         pairingSession, trustStore, sessions);

    CHECK(ackResponse.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(ackResponse.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "internal_error");
    CHECK_FALSE(sessions.IsFullyTrusted(kConnection));
}

TEST_CASE("two separate successful pairing flows issue different credentials",
          "[application][pairing_handler]") {
    PairingSession pairingSession([]() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink sink;
    auto now = std::chrono::steady_clock::now();

    static_cast<void>(HandlePairingRequest(BuildPairingRequestEnvelope(), kSessionId, pairingSession, sink));
    auto firstConfirm =
        HandlePairingConfirm(BuildPairingConfirmEnvelope("123456"), kSessionId, kClientId, pairingSession, now);
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
                                        kConnection, pairingSession, trustStore, sessions));

    static_cast<void>(
        HandlePairingRequest(BuildPairingRequestEnvelope("message-request-2"), kSessionId, pairingSession, sink));
    auto secondConfirm = HandlePairingConfirm(BuildPairingConfirmEnvelope("123456", std::nullopt, "message-confirm-2"),
                                               kSessionId, kClientId, pairingSession, now);
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
    auto response = HandlePairingAck(BuildPairingAckEnvelope(EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9})),
                                      kSessionId, kClientId, kConnection, pairingSession, trustStore, sessions);

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
                                      sessions);

    CHECK(response.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}
