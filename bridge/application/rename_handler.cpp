#include "application/rename_handler.hpp"

#include "protocol/constants.hpp"
#include "protocol/error_payload.hpp"
#include "protocol/rename_outcome_payload.hpp"
#include "protocol/rename_request_payload.hpp"

#include <optional>
#include <utility>

namespace dovahlink::application {

namespace {

/// Builds a response for a decoded rename operation outcome.
protocol::Envelope
BuildRenameOutcome(const std::string &sessionId,
                   const std::string &correlationId, std::string outcome,
                   std::optional<std::string> displayName = std::nullopt) {
  auto envelope = protocol::BuildEnvelope(
      std::string(protocol::message_type::kRenameOutcome), sessionId,
      correlationId,
      protocol::EncodeRenameOutcomePayload(protocol::RenameOutcomePayload{
          .outcome = std::move(outcome),
          .displayName = std::move(displayName)}));
  if (envelope.has_value()) {
    return std::move(*envelope);
  }
  return protocol::BuildErrorEnvelope(correlationId, sessionId,
                                      "internal_error",
                                      "Unable to build response", false);
}

} // namespace

protocol::Envelope
HandleRenameRequest(const protocol::Envelope &renameRequestEnvelope,
                    const std::string &sessionId, const std::string &clientId,
                    security::TrustStore &trustStore) {
  auto request =
      protocol::DecodeRenameRequestPayload(renameRequestEnvelope.payload);
  if (!request.has_value()) {
    return protocol::BuildErrorEnvelope(
        renameRequestEnvelope.messageId, sessionId, "malformed_message",
        "Malformed rename_request payload", false);
  }

  const std::string requestedDisplayName = request->displayName;
  const bool clearsDisplayName = requestedDisplayName.empty();
  auto outcome = trustStore.Rename(clientId, requestedDisplayName);
  switch (outcome) {
  case security::RenameOutcome::kRenamed:
    return BuildRenameOutcome(
        sessionId, renameRequestEnvelope.messageId, "renamed",
        clearsDisplayName ? std::nullopt : std::optional(requestedDisplayName));
  case security::RenameOutcome::kInvalidDisplayName:
    return BuildRenameOutcome(sessionId, renameRequestEnvelope.messageId,
                              "invalid_display_name");
  case security::RenameOutcome::kNotEligible:
  case security::RenameOutcome::kNotFound:
    return BuildRenameOutcome(sessionId, renameRequestEnvelope.messageId,
                              "not_trusted");
  case security::RenameOutcome::kSaveFailed:
    return protocol::BuildErrorEnvelope(renameRequestEnvelope.messageId,
                                        sessionId, "internal_error",
                                        "Unable to save display name", false);
  }

  // Unreachable: every enumerator is handled above.
  return protocol::BuildErrorEnvelope(renameRequestEnvelope.messageId,
                                      sessionId, "internal_error",
                                      "Unable to rename client", false);
}

} // namespace dovahlink::application
