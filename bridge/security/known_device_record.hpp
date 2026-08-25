#pragma once

#include "shared/enums.hpp"

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::security {

///  One persistent Known Device's identity, administration metadata, and current
///  trust state. Always in exactly one of four durable states
///  (`KnownDeviceState`); only `kTrusted` carries a usable `credential`.
struct KnownDeviceRecord {
    ///  The client's stable protocol identity.
    std::string clientId;
    ///  The strong device-scoped credential bound to `clientId`. Empty except
    ///  while `kTrusted`.
    std::vector<std::uint8_t> credential;
    ///  Five-digit administration-only identifier, unique among every currently
    ///  known device regardless of state. Never authentication or authorization
    ///  material. Allocated once, at first successful pairing, and stable for the
    ///  record's entire lifetime.
    std::string shortId;
    ///  Optional bounded, control-character-free, presentation-only label. Never
    ///  authentication or authorization material and never a substitute for
    ///  `clientId`.
    std::optional<std::string> displayName;
    ///  This record's current durable state.
    KnownDeviceState state;
    ///  When this device was first successfully paired. Authoritative for
    ///  age/ordering; never updated after creation.
    std::chrono::system_clock::time_point createdAt;
    ///  When this device was blocked, if it is currently `kBlocked`.
    ///  `std::nullopt` in every other state; cleared on unblock rather than kept
    ///  as block history.
    std::optional<std::chrono::system_clock::time_point> blockedAt;
};

} //  namespace dovahlink::security
