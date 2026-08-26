#pragma once

#include "security/trust_store.hpp"

namespace dovahlink::security {

///  Trust-store operations needed by bulk trust administration.
class ITrustResetStore {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustResetStore() = default;

    ///  Returns every currently trusted device.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListTrusted() = 0;
};

///  Adapts the full trust-store domain to the bulk reset administration port.
class TrustResetStore final : public ITrustResetStore {
  public:
    ///  Binds the adapter to the shared trust-store domain.
    explicit TrustResetStore(ITrustStore& trustStore);

    ///  @copydoc ITrustResetStore::ListTrusted
    [[nodiscard]] std::vector<KnownDeviceRecord> ListTrusted() override;

  private:
    ///  Shared domain that owns trust state and persistence.
    ITrustStore& trustStore_;
};

} //  namespace dovahlink::security
