#include "application/trust_device_admin_service.hpp"

#include <algorithm>
#include <cctype>
#include <map>
#include <sstream>

namespace dovahlink::application {

namespace {

///  Returns the stable, lower-case presentation label for a known-device state.
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

///  Formats a sorted device listing without mutating durable records.
std::string
FormatKnownDeviceListing(std::vector<security::KnownDeviceRecord> records,
                         std::string_view deviceKind,
                         bool includeState = true) {
    if (records.empty()) {
        return "No " + std::string(deviceKind) + "s.";
    }

    std::sort(records.begin(), records.end(),
              [](const security::KnownDeviceRecord& left,
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
    out << records.size() << ' ' << deviceKind << (records.size() == 1 ? "" : "s")
        << ':';
    for (const auto& record : records) {
        std::string displayName = record.displayName.value_or("(no display name)");
        if (record.displayName.has_value() && displayNameCounts[displayName] > 1) {
            displayName += " #" + std::to_string(++displayNameIndexes[displayName]);
        }
        out << '\n'
            << record.shortId << "  " << displayName;
        if (includeState) {
            out << "  " << StateLabel(record.state);
        }
    }
    return out.str();
}

} //  namespace

TrustDeviceAdminService::TrustDeviceAdminService(
    security::ITrustDeviceStore& deviceStore,
    ActiveSessionDisconnector& sessionDisconnector,
    security::IPairingCancellation& pairingCancellation)
    : deviceStore_(deviceStore), sessionDisconnector_(sessionDisconnector),
      pairingCancellation_(pairingCancellation) {}

std::string TrustDeviceAdminService::List(std::string_view scope) const {
    std::string normalizedScope(scope);
    std::transform(normalizedScope.begin(), normalizedScope.end(),
                   normalizedScope.begin(), [](unsigned char character) {
                       return static_cast<char>(std::tolower(character));
                   });

    if (normalizedScope.empty() || normalizedScope == "all") {
        return ListKnownDevices();
    }
    if (normalizedScope == "trust") {
        return ListTrusted();
    }
    if (normalizedScope == "block") {
        return ListBlocked();
    }
    return "Unknown list scope '" + std::string(scope) +
           "'. Use all, trust, or block.";
}

std::string TrustDeviceAdminService::Help() const {
    return "DovahLink commands:\n"
           " list [all|trust|block]\n"
           " revoke -id <id> | block -id <id> | unblock -id <id> | forget -id "
           "<id>\n"
           " reset-trust | reset | confirm-reset -confirm <code> | help";
}

std::string TrustDeviceAdminService::ListTrusted() const {
    return FormatKnownDeviceListing(deviceStore_.ListTrusted(), "trusted client",
                                    false);
}

std::string TrustDeviceAdminService::ListKnownDevices() const {
    return FormatKnownDeviceListing(deviceStore_.ListAll(), "known device");
}

std::string TrustDeviceAdminService::ListBlocked() const {
    auto records = deviceStore_.ListAll();
    records.erase(std::remove_if(records.begin(), records.end(),
                                 [](const security::KnownDeviceRecord& record) {
                                     return record.state !=
                                            security::KnownDeviceState::kBlocked;
                                 }),
                  records.end());
    return FormatKnownDeviceListing(std::move(records), "blocked device");
}

std::string
TrustDeviceAdminService::RevokeByShortId(std::string_view shortId) const {
    auto records = deviceStore_.ListTrusted();
    auto it = std::find_if(records.begin(), records.end(),
                           [&](const security::KnownDeviceRecord& record) {
                               return record.shortId == shortId;
                           });
    if (it == records.end()) {
        return "No trusted client with id " + std::string(shortId) + ".";
    }

    std::string displayName = it->displayName.value_or("(no display name)");
    if (!deviceStore_.Revoke(it->clientId)) {
        return "Failed to revoke client " + std::string(shortId) +
               ": trust-store save failed.";
    }
    sessionDisconnector_.DisconnectIfClientActive(it->clientId, "revoked");
    return "Revoked client " + std::string(shortId) + " (" + displayName + ").";
}

std::string TrustDeviceAdminService::BlockByShortId(
    std::string_view shortId, std::chrono::steady_clock::time_point now) const {
    auto device = deviceStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (deviceStore_.Block(device->clientId)) {
    case security::BlockOutcome::kBlocked:
        (void)pairingCancellation_.TryCancel(device->clientId, now);
        sessionDisconnector_.DisconnectIfClientActive(device->clientId,
                                                      "blocked");
        return "Blocked device " + std::string(shortId) + " (" + displayName + ").";
    case security::BlockOutcome::kAlreadyBlocked:
        return "Device " + std::string(shortId) + " is already blocked.";
    case security::BlockOutcome::kNotEligible:
        return "Device " + std::string(shortId) +
               " cannot be blocked (not currently trusted or revoked).";
    case security::BlockOutcome::kNotFound:
        return "No known device with id " + std::string(shortId) + ".";
    case security::BlockOutcome::kSaveFailed:
        return "Failed to block device " + std::string(shortId) +
               ": trust-store save failed.";
    }
    return "Failed to block device " + std::string(shortId) +
           ": unexpected outcome.";
}

std::string TrustDeviceAdminService::UnblockByShortId(
    std::string_view shortId) const {
    auto device = deviceStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (deviceStore_.Unblock(device->clientId)) {
    case security::UnblockOutcome::kUnblocked:
        return "Unblocked device " + std::string(shortId) + " (" + displayName +
               ").";
    case security::UnblockOutcome::kNotBlocked:
        return "Device " + std::string(shortId) + " is not blocked.";
    case security::UnblockOutcome::kNotFound:
        return "No known device with id " + std::string(shortId) + ".";
    case security::UnblockOutcome::kSaveFailed:
        return "Failed to unblock device " + std::string(shortId) +
               ": trust-store save failed.";
    }
    return "Failed to unblock device " + std::string(shortId) +
           ": unexpected outcome.";
}

std::string TrustDeviceAdminService::ForgetByShortId(
    std::string_view shortId) const {
    auto device = deviceStore_.FindByShortId(shortId);
    if (!device.has_value()) {
        return "No known device with id " + std::string(shortId) + ".";
    }
    std::string displayName = device->displayName.value_or("(no display name)");

    switch (deviceStore_.Forget(device->clientId)) {
    case security::ForgetOutcome::kForgotten:
        return "Forgot device " + std::string(shortId) + " (" + displayName + ").";
    case security::ForgetOutcome::kNotEligible:
        return "Device " + std::string(shortId) +
               " cannot be forgotten (revoke or unblock it first).";
    case security::ForgetOutcome::kNotFound:
        return "No known device with id " + std::string(shortId) + ".";
    case security::ForgetOutcome::kSaveFailed:
        return "Failed to forget device " + std::string(shortId) +
               ": trust-store save failed.";
    }
    return "Failed to forget device " + std::string(shortId) +
           ": unexpected outcome.";
}

} //  namespace dovahlink::application
