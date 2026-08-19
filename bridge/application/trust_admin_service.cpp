#include "application/trust_admin_service.hpp"

#include <algorithm>
#include <chrono>
#include <map>
#include <sstream>

namespace dovahlink::application {

namespace {

/// Returns the stable, lower-case presentation label for a known-device state.
std::string_view StateLabel(security::KnownDeviceState state) {
    switch (state) {
        case security::KnownDeviceState::kTrusted:
            return "trusted";
        case security::KnownDeviceState::kRevoked:
            return "revoked";
        case security::KnownDeviceState::kBlocked:
            return "blocked";
        case security::KnownDeviceState::kUnpaired:
            return "unpaired";
    }
    return "unknown";
}

/// Formats a sorted known-device listing, numbering repeated display names only in the returned
/// presentation and never mutating the durable records.
std::string FormatKnownDeviceListing(std::vector<security::KnownDeviceRecord> records,
                                     std::string_view deviceKind) {
    if (records.empty()) {
        return "No " + std::string(deviceKind) + "s.";
    }

    std::sort(records.begin(), records.end(), [](const security::KnownDeviceRecord& left,
                                                 const security::KnownDeviceRecord& right) {
        if (left.createdAt != right.createdAt) {
            return left.createdAt < right.createdAt;
        }
        return left.shortId < right.shortId;
    });

    std::map<std::string, std::size_t> displayNameCounts;
    for (const auto& record : records) {
        if (record.displayName.has_value()) {
            ++displayNameCounts[*record.displayName];
        }
    }

    std::map<std::string, std::size_t> displayNameIndexes;
    std::ostringstream out;
    out << records.size() << ' ' << deviceKind << (records.size() == 1 ? "" : "s") << ':';
    for (const auto& record : records) {
        std::string displayName = record.displayName.value_or("(no display name)");
        if (record.displayName.has_value() && displayNameCounts[displayName] > 1) {
            displayName += " #" + std::to_string(++displayNameIndexes[displayName]);
        }
        out << '\n' << record.shortId << "  " << displayName << "  " << StateLabel(record.state);
    }
    return out.str();
}

}  // namespace

TrustAdminService::TrustAdminService(security::TrustStore& trustStore, ActiveSessionDisconnector& sessionDisconnector,
                                      security::PairingSession& pairingSession)
    : trustStore_(trustStore), sessionDisconnector_(sessionDisconnector), pairingSession_(pairingSession) {}

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

std::string TrustAdminService::ListKnownDevices() const {
    return FormatKnownDeviceListing(trustStore_.ListAll(), "known device");
}

std::string TrustAdminService::ListBlocked() const {
    auto records = trustStore_.ListAll();
    records.erase(std::remove_if(records.begin(), records.end(), [](const security::KnownDeviceRecord& record) {
                      return record.state != security::KnownDeviceState::kBlocked;
                  }),
                  records.end());
    return FormatKnownDeviceListing(std::move(records), "blocked device");
}

std::string TrustAdminService::RevokeByShortId(std::string_view shortId) const {
    auto records = trustStore_.ListTrusted();
    auto it = std::find_if(records.begin(), records.end(),
                            [&](const security::KnownDeviceRecord& record) { return record.shortId == shortId; });
    if (it == records.end()) {
        return "No trusted client with id " + std::string(shortId) + ".";
    }

    std::string displayName = it->displayName.value_or("(no display name)");
    if (!trustStore_.Revoke(it->clientId)) {
        return "Failed to revoke client " + std::string(shortId) + ": trust-store save failed.";
    }
    sessionDisconnector_.DisconnectIfClientActive(it->clientId);
    return "Revoked client " + std::string(shortId) + " (" + displayName + ").";
}

std::string TrustAdminService::Reset() const {
    std::size_t previousCount = trustStore_.ListTrusted().size();
    if (!trustStore_.Reset()) {
        return "Failed to reset trust: trust-store save failed.";
    }
    sessionDisconnector_.DisconnectActive();
    return "Reset all trust (" + std::to_string(previousCount) +
           (previousCount == 1 ? " client removed)." : " clients removed).");
}

std::string TrustAdminService::BlockByShortId(std::string_view shortId,
                                               std::chrono::steady_clock::time_point now) const {
    auto device = trustStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (trustStore_.Block(device->clientId)) {
        case security::BlockOutcome::kBlocked:
            pairingSession_.TryCancel(device->clientId, now);
            sessionDisconnector_.DisconnectIfClientActive(device->clientId);
            return "Blocked device " + std::string(shortId) + " (" + displayName + ").";
        case security::BlockOutcome::kAlreadyBlocked:
            return "Device " + std::string(shortId) + " is already blocked.";
        case security::BlockOutcome::kNotEligible:
            return "Device " + std::string(shortId) + " cannot be blocked (not currently trusted or revoked).";
        case security::BlockOutcome::kNotFound:
            return "No known device with id " + std::string(shortId) + ".";
        case security::BlockOutcome::kSaveFailed:
            return "Failed to block device " + std::string(shortId) + ": trust-store save failed.";
    }
    // Unreachable: every enumerator is handled above.
    return "Failed to block device " + std::string(shortId) + ": unexpected outcome.";
}

std::string TrustAdminService::UnblockByShortId(std::string_view shortId) const {
    auto device = trustStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (trustStore_.Unblock(device->clientId)) {
        case security::UnblockOutcome::kUnblocked:
            return "Unblocked device " + std::string(shortId) + " (" + displayName + ").";
        case security::UnblockOutcome::kNotBlocked:
            return "Device " + std::string(shortId) + " is not blocked.";
        case security::UnblockOutcome::kNotFound:
            return "No known device with id " + std::string(shortId) + ".";
        case security::UnblockOutcome::kSaveFailed:
            return "Failed to unblock device " + std::string(shortId) + ": trust-store save failed.";
    }
    // Unreachable: every enumerator is handled above.
    return "Failed to unblock device " + std::string(shortId) + ": unexpected outcome.";
}

std::string TrustAdminService::ForgetByShortId(std::string_view shortId) const {
    auto device = trustStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (trustStore_.Forget(device->clientId)) {
        case security::ForgetOutcome::kForgotten:
            return "Forgot device " + std::string(shortId) + " (" + displayName + ").";
        case security::ForgetOutcome::kNotEligible:
            return "Device " + std::string(shortId) + " cannot be forgotten (revoke or unblock it first).";
        case security::ForgetOutcome::kNotFound:
            return "No known device with id " + std::string(shortId) + ".";
        case security::ForgetOutcome::kSaveFailed:
            return "Failed to forget device " + std::string(shortId) + ": trust-store save failed.";
    }
    // Unreachable: every enumerator is handled above.
    return "Failed to forget device " + std::string(shortId) + ": unexpected outcome.";
}

}  // namespace dovahlink::application
