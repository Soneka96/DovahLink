#include "application/rename_handler.hpp"

#include "protocol/error_payload.hpp"
#include "protocol/rename_outcome_payload.hpp"
#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>

#include <cstdint>
#include <optional>
#include <string>
#include <utility>
#include <vector>

using dovahlink::application::HandleRenameRequest;
using dovahlink::protocol::Envelope;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;

namespace {

constexpr const char *kSessionId = "session-1";
constexpr const char *kClientId = "client-1";

/// Persistence double whose future saves can be switched from success to
/// failure.
class ConfigurablePersistence : public ITrustStorePersistence {
public:
  /// Loads an empty trust store.
  std::optional<TrustStoreSnapshot> Load() override {
    return TrustStoreSnapshot{};
  }

  /// Returns the current configured save result.
  bool Save(const TrustStoreSnapshot &) override { return saveSucceeds; }

  /// Controls whether subsequent saves succeed.
  bool saveSucceeds = true;
};

/// Builds a `rename_request` envelope with the requested display name.
Envelope
BuildRenameRequestEnvelope(const std::string &displayName,
                           std::string messageId = "message-rename-1") {
  boost::json::object payload;
  payload["displayName"] = displayName;
  return Envelope{
      .messageType = "rename_request",
      .messageId = std::move(messageId),
      .sessionId = kSessionId,
      .correlationId = std::nullopt,
      .payload = std::move(payload),
  };
}

/// Builds a `rename_request` envelope with an arbitrary JSON value for
/// malformed-payload tests.
Envelope BuildMalformedRenameRequestEnvelope(
    const boost::json::value &displayName,
    std::string messageId = "message-rename-malformed-1") {
  boost::json::object payload;
  payload["displayName"] = displayName;
  return Envelope{
      .messageType = "rename_request",
      .messageId = std::move(messageId),
      .sessionId = kSessionId,
      .correlationId = std::nullopt,
      .payload = std::move(payload),
  };
}

/// Adds one named trusted client to an existing store.
void SeedTrustedStore(TrustStore &store,
                      const std::string &clientId = kClientId,
                      const std::string &displayName = "Old Name") {
  REQUIRE(
      store
          .Persist(clientId, std::vector<std::uint8_t>{1, 2, 3, 4}, displayName)
          .has_value());
}

} // namespace

TEST_CASE("HandleRenameRequest renames a trusted device and returns its new "
          "display name",
          "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore);

  auto response = HandleRenameRequest(BuildRenameRequestEnvelope("New Name"),
                                      kSessionId, kClientId, trustStore);

  CHECK(response.messageType == "rename_outcome");
  auto outcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(response.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "renamed");
  REQUIRE(outcome->displayName.has_value());
  CHECK(*outcome->displayName == "New Name");
  REQUIRE(trustStore.Query(kClientId).has_value());
  CHECK(trustStore.Query(kClientId)->displayName ==
        std::optional<std::string>("New Name"));
}

TEST_CASE("HandleRenameRequest clears a trusted device display name when given "
          "an empty string",
          "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore);

  auto response = HandleRenameRequest(BuildRenameRequestEnvelope(""),
                                      kSessionId, kClientId, trustStore);

  auto outcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(response.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "renamed");
  CHECK_FALSE(outcome->displayName.has_value());
  REQUIRE(trustStore.Query(kClientId).has_value());
  CHECK_FALSE(trustStore.Query(kClientId)->displayName.has_value());
}

TEST_CASE("HandleRenameRequest reports invalid display names without changing "
          "the trusted record",
          "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore);

  auto tooLong = HandleRenameRequest(
      BuildRenameRequestEnvelope(std::string(
          dovahlink::security::kMaxDisplayNameLengthBytes + 1, 'x')),
      kSessionId, kClientId, trustStore);
  auto tooLongOutcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(tooLong.payload);
  REQUIRE(tooLongOutcome.has_value());
  CHECK(tooLongOutcome->outcome == "invalid_display_name");

  auto controlCharacter = HandleRenameRequest(
      BuildRenameRequestEnvelope("bad\nname", "message-rename-2"), kSessionId,
      kClientId, trustStore);
  auto controlOutcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(controlCharacter.payload);
  REQUIRE(controlOutcome.has_value());
  CHECK(controlOutcome->outcome == "invalid_display_name");

  REQUIRE(trustStore.Query(kClientId).has_value());
  CHECK(trustStore.Query(kClientId)->displayName ==
        std::optional<std::string>("Old Name"));
}

TEST_CASE("HandleRenameRequest reports not_trusted for unknown and non-trusted "
          "known devices",
          "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore, "revoked-client");
  REQUIRE(trustStore.Revoke("revoked-client"));
  REQUIRE(trustStore
              .Persist("blocked-client", std::vector<std::uint8_t>{5, 6, 7, 8},
                       std::nullopt)
              .has_value());
  REQUIRE(trustStore.Revoke("blocked-client"));
  CHECK(trustStore.Block("blocked-client") ==
        dovahlink::security::BlockOutcome::kBlocked);
  REQUIRE(trustStore
              .Persist("unpaired-client",
                       std::vector<std::uint8_t>{9, 10, 11, 12}, std::nullopt)
              .has_value());
  REQUIRE(trustStore.Revoke("unpaired-client"));
  CHECK(trustStore.Block("unpaired-client") ==
        dovahlink::security::BlockOutcome::kBlocked);
  CHECK(trustStore.Unblock("unpaired-client") ==
        dovahlink::security::UnblockOutcome::kUnblocked);

  for (const std::string &clientId : {"unknown-client", "revoked-client",
                                      "blocked-client", "unpaired-client"}) {
    auto response =
        HandleRenameRequest(BuildRenameRequestEnvelope("New Name", clientId),
                            kSessionId, clientId, trustStore);
    auto outcome =
        dovahlink::protocol::DecodeRenameOutcomePayload(response.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "not_trusted");
    CHECK_FALSE(outcome->displayName.has_value());
  }
}

TEST_CASE("HandleRenameRequest rejects malformed payloads",
          "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore);

  auto wrongType = HandleRenameRequest(
      BuildMalformedRenameRequestEnvelope(boost::json::value(7)), kSessionId,
      kClientId, trustStore);
  Envelope missingField{
      .messageType = "rename_request",
      .messageId = "message-rename-missing",
      .sessionId = kSessionId,
      .correlationId = std::nullopt,
      .payload = boost::json::object{},
  };
  auto missing =
      HandleRenameRequest(missingField, kSessionId, kClientId, trustStore);

  for (const auto &response : {wrongType, missing}) {
    CHECK(response.messageType == "error");
    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
  }
}

TEST_CASE(
    "HandleRenameRequest reports internal_error and rolls back on save failure",
    "[application][rename_handler]") {
  ConfigurablePersistence persistence;
  auto trustStore = TrustStore::Load(persistence);
  SeedTrustedStore(trustStore);
  persistence.saveSucceeds = false;

  auto response = HandleRenameRequest(BuildRenameRequestEnvelope("New Name"),
                                      kSessionId, kClientId, trustStore);

  CHECK(response.messageType == "error");
  auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
  REQUIRE(error.has_value());
  CHECK(error->code == "internal_error");
  REQUIRE(trustStore.Query(kClientId).has_value());
  CHECK(trustStore.Query(kClientId)->displayName ==
        std::optional<std::string>("Old Name"));
}
