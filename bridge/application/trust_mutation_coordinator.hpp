#pragma once

#include "application/pairing_commit_result.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_mutation_generation.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <cstdint>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::application {

///  Coordinates pending pairing finalization with administrative trust
///  mutations so a stale pairing cannot recreate trust after Block or reset.
class ITrustMutationCoordinator {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustMutationCoordinator() = default;

    ///  Validates the code and creates a pending credential while capturing its
    ///  mutation fence under the same coordination boundary as admin mutations.
    [[nodiscard]] virtual security::ConfirmCodeResult
    ConfirmPairing(const std::string& presentedCode,
                   std::chrono::steady_clock::time_point now,
                   std::string clientId,
                   std::vector<std::uint8_t> credential,
                   std::optional<std::string> displayName) = 0;

    ///  Atomically finalizes the matching pending pairing or preserves it when
    ///  persistence fails.
    [[nodiscard]] virtual PairingCommitResult
    CommitPairing(const std::string& clientId,
                  const std::vector<std::uint8_t>& credential,
                  std::chrono::steady_clock::time_point now) = 0;

    ///  Cancels one pairing under the same coordination boundary as commits.
    [[nodiscard]] virtual security::CancelOutcome
    TryCancel(const std::string& clientId,
              std::chrono::steady_clock::time_point now) = 0;

    ///  Cancels every pairing under the same coordination boundary as commits.
    virtual void CancelAll() = 0;

    ///  Blocks a known device and cancels its pairing only after persistence
    ///  succeeds.
    [[nodiscard]] virtual security::BlockOutcome
    Block(const std::string& clientId,
          std::chrono::steady_clock::time_point now) = 0;

    ///  Performs Reset Trust, returns the affected client IDs, and cancels all
    ///  pairing only after persistence succeeds. An empty optional means the
    ///  trust-store save failed.
    [[nodiscard]] virtual std::optional<std::vector<std::string>>
    ResetTrust() = 0;

    ///  Performs Factory Reset and cancels all pairing only after persistence
    ///  succeeds.
    [[nodiscard]] virtual bool FactoryReset() = 0;
};

///  Real trust-mutation coordinator used by the Bridge composition root.
class TrustMutationCoordinator final : public ITrustMutationCoordinator {
  public:
    ///  Binds the durable trust store and in-memory pairing state machine.
    TrustMutationCoordinator(security::TrustStore& trustStore,
                             security::PairingSession& pairingSession);

    ///  @copydoc ITrustMutationCoordinator::ConfirmPairing
    [[nodiscard]] security::ConfirmCodeResult
    ConfirmPairing(const std::string& presentedCode,
                   std::chrono::steady_clock::time_point now,
                   std::string clientId,
                   std::vector<std::uint8_t> credential,
                   std::optional<std::string> displayName) override;

    ///  @copydoc ITrustMutationCoordinator::CommitPairing
    [[nodiscard]] PairingCommitResult
    CommitPairing(const std::string& clientId,
                  const std::vector<std::uint8_t>& credential,
                  std::chrono::steady_clock::time_point now) override;

    ///  @copydoc ITrustMutationCoordinator::TryCancel
    [[nodiscard]] security::CancelOutcome
    TryCancel(const std::string& clientId,
              std::chrono::steady_clock::time_point now) override;

    ///  @copydoc ITrustMutationCoordinator::CancelAll
    void CancelAll() override;

    ///  @copydoc ITrustMutationCoordinator::Block
    [[nodiscard]] security::BlockOutcome
    Block(const std::string& clientId,
          std::chrono::steady_clock::time_point now) override;

    ///  @copydoc ITrustMutationCoordinator::ResetTrust
    [[nodiscard]] std::optional<std::vector<std::string>>
    ResetTrust() override;

    ///  @copydoc ITrustMutationCoordinator::FactoryReset
    [[nodiscard]] bool FactoryReset() override;

  private:
    ///  Durable trust state owned by the Bridge lifetime.
    security::TrustStore& trustStore_;

    ///  In-memory pairing state owned by the Bridge lifetime.
    security::PairingSession& pairingSession_;

    ///  Serializes all coordinator operations that can consume or invalidate
    ///  pending pairing state.
    std::mutex mutex_;
};

} //  namespace dovahlink::application
