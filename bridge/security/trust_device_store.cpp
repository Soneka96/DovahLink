#include "security/trust_device_store.hpp"

namespace dovahlink::security {

TrustDeviceStore::TrustDeviceStore(ITrustStore& trustStore)
    : trustStore_(trustStore) {}

std::vector<KnownDeviceRecord> TrustDeviceStore::ListTrusted() {
    return trustStore_.ListTrusted();
}

std::vector<KnownDeviceRecord> TrustDeviceStore::ListAll() {
    return trustStore_.ListAll();
}

std::optional<KnownDeviceRecord>
TrustDeviceStore::FindByShortId(std::string_view shortId) {
    return trustStore_.FindByShortId(shortId);
}

UnblockOutcome TrustDeviceStore::Unblock(const std::string& clientId) {
    return trustStore_.Unblock(clientId);
}

ForgetOutcome TrustDeviceStore::Forget(const std::string& clientId) {
    return trustStore_.Forget(clientId);
}

} //  namespace dovahlink::security
