#pragma once

#include "security/trust_store.hpp"

#include <string>
#include <string_view>

namespace dovahlink::application {

/// Reusable trust-administration behavior (list/revoke/reset) over `security::TrustStore`, shared by
/// every administration surface -- console, a future Flutter management UI, and developer tooling --
/// so none of them duplicates trust-store logic (`ai/context/protocol/security.md`'s "Trust
/// administration surface").
///
/// ponytail: `RevokeByShortId`/`Reset` each call `TrustStore` more than once (list-then-act); each
/// individual call is atomic but the pair is not, so a concurrent `Persist`/`Revoke` from an
/// unrelated connection between them could make a reported message (client count, "revoked") stale
/// by the time it prints. `TrustStore`'s own persisted state always stays internally consistent
/// regardless -- only this service's human-readable report could rarely lag. Acceptable because this
/// service is driven by one serialized admin operator at a time (a console command); add a lock
/// spanning both calls if a second concurrent admin surface is ever introduced.
class TrustAdminService {
public:
    /// Binds the service to the trust store it administers.
    /// @param trustStore Persistent trust store this service reads and mutates.
    explicit TrustAdminService(security::TrustStore& trustStore);

    /// Lists every currently trusted client as one display-ready, multi-line string: one
    /// `shortId  displayName` line per client, or a clear "no trusted clients" message when empty.
    [[nodiscard]] std::string ListTrusted() const;

    /// Revokes the trusted client identified by its five-digit `shortId` (never `clientId`, never a
    /// credential -- `security.md`'s own stated purpose for `shortId`).
    /// @param shortId Administration-only identifier presented by the caller.
    /// @return A human-readable result message: not-found, revoked, or a persistence failure.
    [[nodiscard]] std::string RevokeByShortId(std::string_view shortId) const;

    /// Resets all persistent trust.
    /// @return A human-readable result message naming how many clients were removed, or a
    ///     persistence failure.
    [[nodiscard]] std::string Reset() const;

private:
    /// Trust store this service reads and mutates.
    security::TrustStore& trustStore_;
};

}  // namespace dovahlink::application
