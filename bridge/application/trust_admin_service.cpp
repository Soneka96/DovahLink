#include "application/trust_admin_service.hpp"

#include <algorithm>
#include <sstream>

namespace dovahlink::application {

TrustAdminService::TrustAdminService(security::TrustStore& trustStore) : trustStore_(trustStore) {}

std::string TrustAdminService::ListTrusted() const {
    auto records = trustStore_.ListTrusted();
    if (records.empty()) {
        return "No trusted clients.";
    }

    std::ostringstream out;
    out << records.size() << (records.size() == 1 ? " trusted client:" : " trusted clients:");
    for (const auto& record : records) {
        out << '\n' << record.shortId << "  " << record.displayName.value_or("(no display name)");
    }
    return out.str();
}

std::string TrustAdminService::RevokeByShortId(std::string_view shortId) const {
    auto records = trustStore_.ListTrusted();
    auto it = std::find_if(records.begin(), records.end(),
                            [&](const security::TrustedClientRecord& record) { return record.shortId == shortId; });
    if (it == records.end()) {
        return "No trusted client with id " + std::string(shortId) + ".";
    }

    std::string displayName = it->displayName.value_or("(no display name)");
    if (!trustStore_.Revoke(it->clientId)) {
        return "Failed to revoke client " + std::string(shortId) + ": trust-store save failed.";
    }
    return "Revoked client " + std::string(shortId) + " (" + displayName + ").";
}

std::string TrustAdminService::Reset() const {
    std::size_t previousCount = trustStore_.ListTrusted().size();
    if (!trustStore_.Reset()) {
        return "Failed to reset trust: trust-store save failed.";
    }
    return "Reset all trust (" + std::to_string(previousCount) +
           (previousCount == 1 ? " client removed)." : " clients removed).");
}

}  // namespace dovahlink::application
