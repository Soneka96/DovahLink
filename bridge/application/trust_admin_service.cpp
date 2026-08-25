#include "application/trust_admin_service.hpp"

namespace dovahlink::application {

TrustAdminService::TrustAdminService(
    security::ITrustDeviceStore& deviceStore,
    security::ITrustResetStore& resetStore,
    ActiveSessionDisconnector& sessionDisconnector,
    security::IPairingCancellation& pairingCancellation,
    security::IFactoryResetChallenge& factoryResetChallenge)
    : deviceService_(deviceStore, sessionDisconnector, pairingCancellation),
      resetService_(resetStore, sessionDisconnector, pairingCancellation,
                    factoryResetChallenge) {}

TrustAdminService::TrustAdminService(
    security::TrustStore& trustStore,
    ActiveSessionDisconnector& sessionDisconnector,
    security::PairingSession& pairingSession,
    security::FactoryResetChallenge& factoryResetChallenge)
    : deviceService_(trustStore, sessionDisconnector, pairingSession),
      resetService_(trustStore, sessionDisconnector, pairingSession,
                    factoryResetChallenge) {}

std::string TrustAdminService::List(std::string_view scope) const {
    return deviceService_.List(scope);
}

std::string TrustAdminService::Help() const { return deviceService_.Help(); }

std::string TrustAdminService::ListTrusted() const {
    return deviceService_.ListTrusted();
}

std::string TrustAdminService::ListKnownDevices() const {
    return deviceService_.ListKnownDevices();
}

std::string TrustAdminService::ListBlocked() const {
    return deviceService_.ListBlocked();
}

std::string
TrustAdminService::RevokeByShortId(std::string_view shortId) const {
    return deviceService_.RevokeByShortId(shortId);
}

std::string TrustAdminService::StartFactoryReset() const {
    return resetService_.StartFactoryReset();
}

std::string TrustAdminService::BlockByShortId(
    std::string_view shortId, std::chrono::steady_clock::time_point now) const {
    return deviceService_.BlockByShortId(shortId, now);
}

std::string
TrustAdminService::UnblockByShortId(std::string_view shortId) const {
    return deviceService_.UnblockByShortId(shortId);
}

std::string TrustAdminService::ForgetByShortId(std::string_view shortId) const {
    return deviceService_.ForgetByShortId(shortId);
}

std::string
TrustAdminService::ConfirmFactoryReset(std::string_view presentedCode) const {
    return resetService_.ConfirmFactoryReset(presentedCode);
}

std::string TrustAdminService::ResetTrust() const {
    return resetService_.ResetTrust();
}

} //  namespace dovahlink::application
