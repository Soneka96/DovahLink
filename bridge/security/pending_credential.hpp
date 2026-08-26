#pragma once

#include "security/trust_mutation_generation.hpp"

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::security {

///  The credential-issuance data a successful `PairingSession::TryFinalize`
///  returns, ready for the caller to commit via `TrustStore::Persist`.
struct PendingCredential {
    ///  The pairing client's stable protocol identity.
    std::string clientId;
    ///  The strong credential the Bridge generated for this pairing attempt.
    std::vector<std::uint8_t> credential;
    ///  The optional presentation-only label the client supplied with its code.
    std::optional<std::string> displayName;
    ///  Trust-store mutation generation captured when this credential became
    ///  pending.
    TrustMutationGeneration mutationGeneration{};
    ///  When this credential entered `PENDING_CREDENTIAL`, for
    ///  `kPairingPendingCredentialTtl`'s lazy-expiry check in
    ///  `PeekPending`/`CommitPending`/`TryStartChallenge`/`TryCancel`.
    std::chrono::steady_clock::time_point pendingSince;
};

} //  namespace dovahlink::security
