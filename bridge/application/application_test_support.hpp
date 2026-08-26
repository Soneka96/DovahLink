#pragma once

#include "application/active_play_context_level_sink.hpp"
#include "application/active_play_context_reader.hpp"
#include "application/active_session_disconnector.hpp"
#include "application/play_context.hpp"
#include "application/play_context_lifecycle.hpp"
#include "application/session.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "game_state/level_adapter.hpp"
#include "protocol/envelope.hpp"
#include "security/factory_reset_challenge.hpp"
#include "security/known_device_record.hpp"
#include "security/test_token.hpp"
#include "security/trust_store.hpp"

#include <boost/json/object.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace dovahlink::application::test_support {

///  Builds a representative authenticated envelope with caller-controlled
///  protocol type, identity, and payload values.
inline protocol::Envelope BuildEnvelope(
    std::string messageType = "ping", std::string messageId = "message-1",
    std::optional<std::string> sessionId = std::string("session-1"),
    std::optional<std::string> correlationId = std::nullopt,
    boost::json::object payload = {}) {
    return protocol::Envelope{
        .messageType = std::move(messageType),
        .messageId = std::move(messageId),
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = std::move(payload),
    };
}

///  Builds a representative client `hello` envelope for application tests.
///
///  The optional credential is omitted when it is `std::nullopt`, which allows
///  callers to exercise authentication methods whose payload has no token or a
///  missing-token validation path without rebuilding the envelope shape.
inline protocol::Envelope BuildHelloEnvelope(
    std::optional<std::string> credential =
        std::string(security::kValidHexToken),
    std::string messageId = "message-hello-1",
    std::optional<std::string> clientId = std::string("client-1"),
    std::string authMethod = "one_time_local_token") {
    boost::json::object payload;
    payload["endpoint"] = "client";
    if (clientId.has_value()) {
        payload["clientId"] = *clientId;
    }

    boost::json::object auth;
    auth["method"] = std::move(authMethod);
    if (credential.has_value()) {
        auth["token"] = *credential;
    }
    payload["auth"] = std::move(auth);

    return protocol::Envelope{
        .messageType = "hello",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

///  Builds a representative `pairing_request` envelope.
inline protocol::Envelope BuildPairingRequestEnvelope(
    std::string messageId = "message-request-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_request", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `pairing_confirm` envelope with an optional display
///  name.
inline protocol::Envelope BuildPairingConfirmEnvelope(
    const std::string& code, std::optional<std::string> displayName = std::nullopt,
    std::string messageId = "message-confirm-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["code"] = code;
    payload["displayName"] = displayName.has_value()
                                 ? boost::json::value(*displayName)
                                 : boost::json::value(nullptr);
    return BuildEnvelope("pairing_confirm", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

///  Builds a representative `pairing_ack` envelope for a hex credential.
inline protocol::Envelope BuildPairingAckEnvelope(
    const std::string& hexCredential, std::string messageId = "message-ack-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["credential"] = hexCredential;
    return BuildEnvelope("pairing_ack", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

///  Builds a representative `pairing_renotify` envelope.
inline protocol::Envelope BuildPairingRenotifyEnvelope(
    std::string messageId = "message-renotify-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_renotify", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `pairing_cancel` envelope.
inline protocol::Envelope BuildPairingCancelEnvelope(
    std::string messageId = "message-cancel-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_cancel", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `rename_request` envelope with a display name.
inline protocol::Envelope BuildRenameRequestEnvelope(
    const std::string& displayName, std::string messageId = "message-rename-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["displayName"] = displayName;
    return BuildEnvelope("rename_request", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

//  ---- Application fixtures ----

///  Builds a fresh play context with an empty authoritative state store.
inline std::shared_ptr<PlayContext> BuildPlayContext(
    std::string id = "context-1") {
    return std::make_shared<PlayContext>(std::move(id));
}

///  Builds a representative known-device record for application service tests.
inline security::KnownDeviceRecord BuildKnownDeviceRecord(
    std::string clientId = "client-1", std::string shortId = "11111",
    std::optional<std::string> displayName = std::nullopt,
    security::KnownDeviceState state = security::KnownDeviceState::kTrusted,
    int createdAtSeconds = 1) {
    return security::KnownDeviceRecord{
        .clientId = std::move(clientId),
        .credential = state == security::KnownDeviceState::kTrusted
                          ? std::vector<std::uint8_t>{1, 2}
                          : std::vector<std::uint8_t>{},
        .shortId = std::move(shortId),
        .displayName = std::move(displayName),
        .state = state,
        .createdAt = std::chrono::system_clock::time_point(
            std::chrono::seconds(createdAtSeconds)),
    };
}

//  ---- Reusable contract mocks ----

///  GoogleMock active play-context identity contract double.
class MockActivePlayContext : public IActivePlayContextReader {
  public:
    MOCK_METHOD(std::optional<std::string>, CurrentPlayContextId, (),
                (const, override));
};

///  Stateless fake for consumers that do not exercise context identity.
class EmptyActivePlayContext final : public IActivePlayContextReader {
  public:
    ///  Reports that no play context is active.
    [[nodiscard]] std::optional<std::string>
    CurrentPlayContextId() const override {
        return std::nullopt;
    }
};

///  GoogleMock lifecycle aggregate contract double.
class MockPlayContextLifecycle : public IPlayContextLifecycle {
  public:
    MOCK_METHOD(PlayContextTransition, HandleEvent, (LifecycleEvent),
                (override));
    MOCK_METHOD(std::optional<std::string>, CurrentPlayContextId, (),
                (const, override));
    MOCK_METHOD(LifecycleState, CurrentState, (), (const, override));
    MOCK_METHOD(void, CaptureLevel, (std::optional<std::int64_t>), (override));
};

///  GoogleMock player-level accessor contract double.
class MockPlayerLevelAccessor : public game_state::IPlayerLevelAccessor {
  public:
    MOCK_METHOD(std::optional<std::int64_t>, ReadLevel, (),
                (const, override));
};

///  GoogleMock active-context level-sink contract double.
class MockActivePlayContextLevelSink : public IActivePlayContextLevelSink {
  public:
    MOCK_METHOD(void, OnLevelCaptured, (std::optional<std::int64_t>),
                (override));
};

///  GoogleMock active-session disconnection contract double.
class MockActiveSessionDisconnector : public ActiveSessionDisconnector {
  public:
    MOCK_METHOD(void, DisconnectIfClientActive,
                (std::string_view, std::string_view), (override));
    MOCK_METHOD(void, DisconnectActive, (std::string_view), (override));
};

///  GoogleMock per-device trust-store contract double.
class MockTrustDeviceStore : public security::ITrustDeviceStore {
  public:
    MOCK_METHOD(std::vector<security::KnownDeviceRecord>, ListTrusted, (),
                (override));
    MOCK_METHOD(std::vector<security::KnownDeviceRecord>, ListAll, (),
                (override));
    MOCK_METHOD(std::optional<security::KnownDeviceRecord>, FindByShortId,
                (std::string_view), (override));
    MOCK_METHOD(bool, Revoke, (const std::string&), (override));
    MOCK_METHOD(security::BlockOutcome, Block, (const std::string&),
                (override));
    MOCK_METHOD(security::UnblockOutcome, Unblock, (const std::string&),
                (override));
    MOCK_METHOD(security::ForgetOutcome, Forget, (const std::string&),
                (override));
};

///  GoogleMock bulk trust-reset store contract double.
class MockTrustResetStore : public security::ITrustResetStore {
  public:
    MOCK_METHOD(std::vector<security::KnownDeviceRecord>, ListTrusted, (),
                (override));
    MOCK_METHOD(bool, Reset, (), (override));
    MOCK_METHOD(bool, ResetTrust, (), (override));
};

///  GoogleMock trust-mutation coordination contract double.
class MockTrustMutationCoordinator : public ITrustMutationCoordinator {
  public:
    MOCK_METHOD(security::ConfirmCodeResult, ConfirmPairing,
                (const std::string&, std::chrono::steady_clock::time_point,
                 std::string, std::vector<std::uint8_t>,
                 std::optional<std::string>),
                (override));
    MOCK_METHOD(PairingCommitResult, CommitPairing,
                (const std::string&, const std::vector<std::uint8_t>&,
                 std::chrono::steady_clock::time_point, ConnectionId,
                 const std::string&),
                (override));
    MOCK_METHOD(std::optional<security::KnownDeviceRecord>,
                PromoteAlreadyTrusted,
                (const std::string&, const std::vector<std::uint8_t>&,
                 ConnectionId, const std::string&),
                (override));
    MOCK_METHOD(security::CancelOutcome, TryCancel,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(void, CancelAll, (), (override));
    MOCK_METHOD(security::BlockOutcome, Block,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(bool, Revoke,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(std::optional<std::vector<std::string>>, ResetTrust, (),
                (override));
    MOCK_METHOD(bool, FactoryReset, (), (override));
};

///  GoogleMock Factory Reset challenge contract double.
class MockFactoryResetChallenge : public security::IFactoryResetChallenge {
  public:
    MOCK_METHOD(std::optional<std::string>, TryStart, (), (override));
    MOCK_METHOD(std::chrono::steady_clock::duration, CodeTimeToLive, (),
                (const, override));
    MOCK_METHOD(security::FactoryResetConfirmOutcome, TryConfirm,
                (const std::string&), (override));
};

} //  namespace dovahlink::application::test_support
