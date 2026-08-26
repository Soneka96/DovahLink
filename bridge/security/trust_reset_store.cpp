#include "security/trust_reset_store.hpp"

namespace dovahlink::security {

TrustResetStore::TrustResetStore(ITrustStore& trustStore)
    : trustStore_(trustStore) {}

std::vector<KnownDeviceRecord> TrustResetStore::ListTrusted() {
    return trustStore_.ListTrusted();
}

} //  namespace dovahlink::security
