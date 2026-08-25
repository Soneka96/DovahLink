#pragma once

#include "security/known_device_record.hpp"
#include "shared/enums.hpp"

#include <optional>
#include <utility>

namespace dovahlink::application {

///  Result returned when the trust-mutation coordinator finalizes a pending
///  pairing credential.
class PairingCommitResult {
  public:
    ///  Creates a result for a successfully committed pairing.
    [[nodiscard]] static PairingCommitResult
    Committed(security::KnownDeviceRecord record) {
        return PairingCommitResult(security::PairingCommitOutcome::kCommitted,
                                   std::move(record));
    }

    ///  Creates a result for a missing pending pairing.
    [[nodiscard]] static PairingCommitResult PendingNotFound() {
        return PairingCommitResult(
            security::PairingCommitOutcome::kPendingNotFound, std::nullopt);
    }

    ///  Creates a result for a retryable trust-store persistence failure.
    [[nodiscard]] static PairingCommitResult PersistenceFailed() {
        return PairingCommitResult(
            security::PairingCommitOutcome::kPersistenceFailed, std::nullopt);
    }

    ///  Creates a result for a pending pairing invalidated by administration.
    [[nodiscard]] static PairingCommitResult Invalidated() {
        return PairingCommitResult(security::PairingCommitOutcome::kInvalidated,
                                   std::nullopt);
    }

    ///  Returns the finalization outcome.
    [[nodiscard]] security::PairingCommitOutcome Outcome() const noexcept {
        return outcome_;
    }

    ///  Returns the newly trusted record, present only for `kCommitted`.
    [[nodiscard]] const std::optional<security::KnownDeviceRecord>&
    Record() const noexcept {
        return record_;
    }

  private:
    ///  Stores one validated result state and its optional committed record.
    PairingCommitResult(
        security::PairingCommitOutcome outcome,
        std::optional<security::KnownDeviceRecord> record)
        : outcome_(outcome), record_(std::move(record)) {}

    ///  Whether the pending credential was committed, missing, preserved after a
    ///  persistence failure, or invalidated by an administrative mutation.
    security::PairingCommitOutcome outcome_;

    ///  The newly trusted record, populated only for `kCommitted`.
    std::optional<security::KnownDeviceRecord> record_;
};

} //  namespace dovahlink::application
