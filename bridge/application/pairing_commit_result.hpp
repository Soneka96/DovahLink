#pragma once

#include "security/known_device_record.hpp"
#include "shared/enums.hpp"

#include <optional>

namespace dovahlink::application {

///  Result returned when the trust-mutation coordinator finalizes a pending
///  pairing credential.
struct PairingCommitResult {
    ///  Whether the pending credential was committed, missing, preserved after a
    ///  persistence failure, or invalidated by an administrative mutation.
    security::PairingCommitOutcome outcome;

    ///  The newly trusted record, populated only for `kCommitted`.
    std::optional<security::KnownDeviceRecord> record;
};

} //  namespace dovahlink::application
