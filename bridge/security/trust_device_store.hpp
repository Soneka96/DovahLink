#pragma once

#include "security/trust_store.hpp"

namespace dovahlink::security {

///  Trust-store operations needed by per-device administration.
class ITrustDeviceStore {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustDeviceStore() = default;

    ///  Returns every currently trusted device.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListTrusted() = 0;

    ///  Returns every known device regardless of state.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListAll() = 0;

    ///  Finds a known device by its administration-only short identifier.
    [[nodiscard]] virtual std::optional<KnownDeviceRecord>
    FindByShortId(std::string_view shortId) = 0;

    ///  Unblocks a known client.
    [[nodiscard]] virtual UnblockOutcome
    Unblock(const std::string& clientId) = 0;

    ///  Forgets an eligible known client.
    [[nodiscard]] virtual ForgetOutcome
    Forget(const std::string& clientId) = 0;
};

///  Adapts the full trust-store domain to the per-device administration port.
class TrustDeviceStore final : public ITrustDeviceStore {
  public:
    ///  Binds the adapter to the shared trust-store domain.
    explicit TrustDeviceStore(ITrustStore& trustStore);

    ///  @copydoc ITrustDeviceStore::ListTrusted
    [[nodiscard]] std::vector<KnownDeviceRecord> ListTrusted() override;

    ///  @copydoc ITrustDeviceStore::ListAll
    [[nodiscard]] std::vector<KnownDeviceRecord> ListAll() override;

    ///  @copydoc ITrustDeviceStore::FindByShortId
    [[nodiscard]] std::optional<KnownDeviceRecord>
    FindByShortId(std::string_view shortId) override;

    ///  @copydoc ITrustDeviceStore::Unblock
    [[nodiscard]] UnblockOutcome Unblock(const std::string& clientId) override;

    ///  @copydoc ITrustDeviceStore::Forget
    [[nodiscard]] ForgetOutcome Forget(const std::string& clientId) override;

  private:
    ///  Shared domain that owns trust state and persistence.
    ITrustStore& trustStore_;
};

} //  namespace dovahlink::security
