#pragma once

#include "security/limits.hpp"
#include "security/token_store.hpp"
#include "shared/enums.hpp"

#include <chrono>
#include <functional>
#include <mutex>
#include <optional>
#include <string>

namespace dovahlink::security {

/// In-memory, single global six-digit confirmation challenge for the destructive Factory Reset
/// operation, per `ai/context/protocol/security.md`'s "Administrative session invalidation" and
/// `roadmap/03-local-device-pairing-and-reconnection.md`'s "3.2 Known Device & Trust
/// Administration". Unlike `PairingSession`, there is no per-`clientId` ownership, pacing, or
/// renotify concept: Factory Reset is a single local admin operation, started and confirmed
/// through the same trusted console/Papyrus surface, never over the wire (`security.md`: "remote
/// clients cannot confirm it"). A wrong code destroys the challenge immediately rather than
/// counting toward a multi-attempt budget. Never persists across a Bridge restart, matching
/// `PairingSession`'s own in-memory-only lifecycle.
class FactoryResetChallenge {
public:
    /// Produces one six-digit confirmation-code candidate, or `std::nullopt` when the underlying
    /// random source fails.
    using CodeGenerator = std::function<std::optional<std::string>()>;

    /// Creates a challenge holder with no active challenge.
    /// @param codeGenerator Produces each six-digit code `TryStart` issues; defaults to the real
    ///     CSPRNG-backed generator. Tests inject a deterministic generator here.
    /// @param codeTimeToLive How long a started challenge's code remains valid. Tests use a short
    ///     or zero duration to exercise expiry deterministically.
    explicit FactoryResetChallenge(CodeGenerator codeGenerator = DefaultCodeGenerator,
                                    std::chrono::steady_clock::duration codeTimeToLive = kFactoryResetCodeTtl);

    /// Generates one six-digit confirmation code using the system CSPRNG.
    [[nodiscard]] static std::optional<std::string> DefaultCodeGenerator();

    /// Starts a fresh challenge unconditionally, discarding any previous one -- there is no
    /// ownership model to resume or contend over, since this is driven by one serialized local
    /// admin operator (matching `TrustAdminService`'s own documented assumption). Performs no
    /// mutation of any trust state; the caller is responsible for displaying the returned code.
    /// @return The six-digit code to display, or `std::nullopt` when the underlying random source
    ///     fails.
    [[nodiscard]] std::optional<std::string> TryStart();

    /// Validates `presentedCode` against the active challenge (constant-time, single-use).
    /// @param presentedCode The code the operator entered.
    /// @return `kConfirmed` on a match, consuming the challenge; `kExpired` when no challenge is
    ///     currently active; `kInvalid` when the code does not match, which also destroys the
    ///     challenge outright.
    [[nodiscard]] FactoryResetConfirmOutcome TryConfirm(const std::string& presentedCode);

private:
    /// Serializes access to `activeChallenge_`.
    std::mutex mutex_;

    /// Produces each six-digit code candidate.
    CodeGenerator codeGenerator_;

    /// How long a newly started challenge's code remains valid.
    std::chrono::steady_clock::duration codeTimeToLive_;

    /// The active challenge's single-use, expiring code, or no value when none is active.
    std::optional<TokenStore> activeChallenge_;
};

}  // namespace dovahlink::security
